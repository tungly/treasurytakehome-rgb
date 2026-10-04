using System.Text.Json;
using OpenAI.Chat;

namespace LabelVerify;

/// <summary>Reads the label fields out of a label image with one Azure OpenAI vision call.</summary>
public class LabelReader(ChatClient chat)
{
    const string Instructions = """
        You read alcohol beverage label images for US TTB compliance review.
        Copy each field exactly as printed on the label. Do not fix typos, spelling, capital letters, or punctuation.
        Use null for any field that is not on the label or that you cannot read.

        - brandName: the brand name.
        - classType: the class or type designation, such as "Kentucky Straight Bourbon Whiskey".
        - alcoholContent: the alcohol statement as printed, such as "45% Alc./Vol. (90 Proof)".
        - netContents: the net contents as printed, such as "750 mL".
        - bottler: the bottler, producer, or importer name and address. Leave out lead-in words like "Bottled by".
        - countryOfOrigin: the country name only, only if the label states one. Leave out words like "Product of".
        - governmentWarning: the whole warning statement, word for word, keeping its exact capital letters.
          Start with its opening words exactly as printed (for example "GOVERNMENT WARNING:") and include everything after them.
          Never fill in words you cannot see. Write [unreadable] in place of any part you cannot read.
        - warningHeaderBold: true if the words "GOVERNMENT WARNING" are in bold type, false if not, null if you cannot tell.
        - warningBodyBold: true if the rest of the warning is in bold type, false if not, null if you cannot tell.
        - warningFullyReadable: true if every word of the warning is clearly readable in this image. false if glare, blur,
          shadow, damage, or cropping hides or blurs any part of it, even if you could guess the missing words. null if there is no warning.
        - warningTooSmall: compare the height of the warning's letters with the smallest other text on the label
          (often the bottler or address line). true if the warning letters are clearly smaller than that text,
          or the warning is crammed, faint, or hard to find. false if they are about the same size or larger. null if you cannot tell.
        """;

    static readonly ChatResponseFormat Format = ChatResponseFormat.CreateJsonSchemaFormat("label", BinaryData.FromString("""
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["brandName", "classType", "alcoholContent", "netContents", "bottler",
                           "countryOfOrigin", "governmentWarning", "warningHeaderBold", "warningBodyBold",
                           "warningFullyReadable", "warningTooSmall"],
              "properties": {
                "brandName": { "type": ["string", "null"] },
                "classType": { "type": ["string", "null"] },
                "alcoholContent": { "type": ["string", "null"] },
                "netContents": { "type": ["string", "null"] },
                "bottler": { "type": ["string", "null"] },
                "countryOfOrigin": { "type": ["string", "null"] },
                "governmentWarning": { "type": ["string", "null"] },
                "warningHeaderBold": { "type": ["boolean", "null"] },
                "warningBodyBold": { "type": ["boolean", "null"] },
                "warningFullyReadable": { "type": ["boolean", "null"] },
                "warningTooSmall": { "type": ["boolean", "null"] }
              }
            }
            """), jsonSchemaIsStrict: true);

    public async Task<ExtractedLabel> ReadAsync(byte[] image, string mediaType, CancellationToken ct = default)
    {
        ChatMessage[] messages =
        [
            new SystemChatMessage(Instructions),
            new UserChatMessage(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(image), mediaType, ChatImageDetailLevel.High)),
        ];
        // New options every call: the SDK writes each request's messages onto this object, so sharing it mixes up labels.
        var options = new ChatCompletionOptions { Temperature = 0, ResponseFormat = Format };
        ChatCompletion completion = await chat.CompleteChatAsync(messages, options, ct);
        return JsonSerializer.Deserialize<ExtractedLabel>(completion.Content[0].Text, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The model returned an empty answer.");
    }
}
