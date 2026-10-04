# Label Check

Label Check compares an alcohol label image with what the label application says. It reads the label with an AI vision model, then checks each field with plain, testable rules and shows **Match**, **Needs review**, or **Mismatch** for each one. Agents can check one label or a whole batch.

**Live prototype:** https://labelverify-16883.azurewebsites.net

The assignment brief is in [docs/assignment.md](docs/assignment.md).

## Using the app

### Check one label

1. Open the app. **Check one label** is the first page.
2. Choose the label images: front, back, and neck if they are separate (up to 4 images, JPG or PNG, 10 MB each). Most bottles print the government warning and bottler on the back, so a front-only photo will show the warning as missing.
   Each photo then appears with **Turn left**, **Turn right**, **Draw box** and **Clear box** buttons. If text on a photo is sideways, turn it until it reads normally. To help with small print, press **Draw box** and drag across the label to mark it; with a mouse you can drag right away. On a phone or tablet, a finger on a photo scrolls the page until **Draw box** is pressed. Both steps are optional, and the result page shows the photos exactly as they were read.
3. Type in what the application says: brand name, class/type, alcohol content, net contents, and bottler name and address. Fill in the country of origin only for imports.
4. Press **Check label**. The result usually appears in about 2 to 3 seconds. The page shows the total wait and how long reading the label took.

The government warning is always checked against the required wording, so there is nothing to type for it.

### Check a batch

1. Open **Check a batch**.
2. Choose all the label images at once.
3. Choose a CSV file with one row per label. Use **Download a CSV template** on the page, or see [samples/applications.csv](samples/applications.csv).
4. Press **Check all labels**. Keep the page open until the batch is done: results live only in the page, and a 300-label batch takes about 4 minutes. Results fill in as each label finishes. Use **Show only labels that need attention** to hide the labels that match, and **Download results (CSV)** to save them.

CSV columns: `file`, `brand_name`, `class_type`, `alcohol_content`, `net_contents`, `bottler`, `country_of_origin`. The `file` column lists the label's image file names. Separate several images of one label with `|`, for example `front.jpg|back.jpg`. Leave `country_of_origin` blank for domestic products.

### Try it with the sample files

The [samples](samples/) folder has 11 test labels (12 images) and a matching CSV. Upload all 12 images and `applications.csv` on the batch page.

| Sample | Expected result |
|---|---|
| `old-tom-good.png` | Everything matches |
| `old-tom-title-case-warning.png` | Mismatch: "Government Warning" is not in capitals |
| `stones-throw-wrong-abv.png` | Mismatch: alcohol content is 40%, application says 45%. Brand "STONE'S THROW" still matches "Stone's Throw" |
| `highland-import-bold-body.png` | Needs review: the whole warning is bold, but only the header may be |
| `old-tom-tiny-warning.png` | Needs review: the warning is printed much smaller than the other text |
| `photo-old-tom-glare-on-warning.jpg` | Needs review: glare hides part of the warning, so the wording cannot be trusted |
| `photo-old-tom-title-case-dim-blurry.jpg` | Mismatch: same title-case warning, in a dim, blurry photo |
| Other `photo-old-tom-*.jpg` | Everything matches: the good label tilted, with glare on the brand, and dim and blurry |
| `old-tom-front.png` + `old-tom-back.png` | Everything matches: brand and alcohol on the front, bottler and warning on the back. The front alone gives a Mismatch, because the warning is missing |

## Running it locally

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An Azure OpenAI resource with a vision model deployment. This prototype uses `gpt-4.1-mini`.
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) if you want to create the resource or deploy from the command line.

### Set up and run

Save the Azure OpenAI settings with .NET user-secrets. They stay outside the repository.

```bash
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://<your-resource>.openai.azure.com/" --project src/LabelVerify
dotnet user-secrets set "AzureOpenAI:Deployment" "<your-deployment-name>" --project src/LabelVerify
dotnet user-secrets set "AzureOpenAI:Key" "<your-key>" --project src/LabelVerify
```

Start the app, then open the address it prints:

```bash
dotnet run --project src/LabelVerify
```

Run the tests (no Azure access needed):

```bash
dotnet test
```

Run every sample through a running app and compare each result with the expected one (needs Python 3 and curl; each label is one Azure OpenAI call). It exits with status 1 if any result differs. Samples marked `judgment` depend on the model's judgment of bold, readability or type size and can occasionally flip:

```bash
python3 scripts/smoke_test.py                                            # app started with dotnet run
python3 scripts/smoke_test.py https://labelverify-16883.azurewebsites.net
```

The unit tests cover the comparison rules and the CSV reader. The smoke test covers the whole path through the single-label page and Azure OpenAI. Neither covers the batch page's browser code, which was tested by hand.

Recreate the sample images (needs Python with Pillow and macOS Arial fonts):

```bash
python3 samples/make_labels.py && python3 samples/make_photos.py
```

### Create the Azure OpenAI resource

```bash
az group create --name label-verify --location eastus2
az cognitiveservices account create --name <resource-name> --resource-group label-verify --location eastus2 --kind OpenAI --sku S0 --custom-domain <resource-name> --yes
az cognitiveservices account deployment create --name <resource-name> --resource-group label-verify --deployment-name label-reader --model-name gpt-4.1-mini --model-version 2025-04-14 --model-format OpenAI --sku-name GlobalStandard --sku-capacity 200
```

If `account create` fails with `MissingSubscriptionRegistration`, run `az provider register --namespace Microsoft.CognitiveServices --wait` first.

## Deploying to Azure App Service

```bash
az provider register --namespace Microsoft.Web --wait
az appservice plan create --name label-verify-plan --resource-group label-verify --location centralus --is-linux --sku B1
az webapp create --name <app-name> --resource-group label-verify --plan label-verify-plan --runtime "DOTNETCORE:10.0" --https-only true
az webapp config set --name <app-name> --resource-group label-verify --always-on true
az webapp config appsettings set --name <app-name> --resource-group label-verify --output none --settings \
  AzureOpenAI__Endpoint="https://<resource-name>.openai.azure.com/" AzureOpenAI__Deployment=label-reader \
  AzureOpenAI__Key="$(az cognitiveservices account keys list --name <resource-name> --resource-group label-verify --query key1 -o tsv)"
dotnet publish src/LabelVerify -c Release -o ./publish && (cd publish && zip -qr ../app.zip .)
az webapp deploy --name <app-name> --resource-group label-verify --src-path app.zip --type zip
```

Always On keeps the app loaded, so the first check after a quiet period is not slowed by a restart. App Service reads the settings as environment variables. The double underscore (`__`) stands for the `:` in the setting names.

The plan is in Central US because the free-trial subscription had no B1 quota in East US or East US 2. If `plan create` fails with "Operation cannot be completed without additional quota", try another region. The app and the Azure OpenAI resource do not need to be in the same region.

## How it works

1. **Read.** The browser first shrinks large photos to 2048 pixels on the long side, so a 2 MB phone photo uploads as about 0.3 MB. All images of one label (front, back, neck) then go to Azure OpenAI together in one call ([LabelReader.cs](src/LabelVerify/LabelReader.cs)). The model must answer in a fixed JSON shape, at temperature 0, and is told to copy text exactly as printed, without fixing typos or capital letters.
2. **Compare.** Plain C# code compares each field with the application ([LabelCheck.cs](src/LabelVerify/LabelCheck.cs)). The model never decides pass or fail, so every rule is unit tested and easy to explain.
3. **Show.** The page shows each field side by side with its result and a short reason.

### Rules for each field

| Field | Rule |
|---|---|
| Brand, class/type, bottler, country | Ignores case, punctuation and spacing, so "STONE'S THROW" matches "Stone's Throw". A near miss (a typo, or one value inside the other) goes to **Needs review**. |
| Bottler (extra rule) | If every word of the application appears on the label in the same order, it is a **Match**, even with other text around or between them, such as a producer line before "Imported by", "Distilled in Indiana", or a street address between the name and the town. At least two words are needed. For brand and class/type, extra words still go to **Needs review**, because they can mean a different product. |
| Alcohol content | Compares the percentages. If the label shows proof, it must be twice the ABV. |
| Net contents | Converts both to mL first, so "750 mL" matches "75 cL" and "12 fl oz" matches "355 mL" (within 0.5%). |
| Government warning wording | Must match 27 CFR 16.21 word for word, and "GOVERNMENT WARNING:" must be in capitals (27 CFR 16.22). Line breaks, extra spaces and line-break hyphens ("PREG-NANCY") are forgiven. The rest of the warning may be printed all in capitals, because the regulation sets capitals only for "GOVERNMENT WARNING". A different word is a **Mismatch** and the note names it. If only punctuation or other capital letters differ, it goes to **Needs review**, because small print in photos often loses punctuation; the note names the spot. |
| Government warning bold type | "GOVERNMENT WARNING:" must be bold and the rest must not be (27 CFR 16.22). Problems go to **Needs review**, because bold is judged from the image. |
| Unreadable fields | The model lists any field it could not read with certainty (glare, blur, angle, small print, cropping). Those fields go to **Needs review** whatever the comparison said, because a guessed reading can match or mismatch by chance. |
| Government warning readability | If glare, blur or cropping hides any part of the warning, the wording result becomes **Needs review**, because the model may have guessed the hidden words. The model is told to write `[unreadable]` instead of guessing. |
| Government warning type size | If the warning letters look clearly smaller than the smallest other text, or the warning is faint or hard to find, it goes to **Needs review**. A photo has no scale, so this is a judgment, not a measurement of the 27 CFR 16.22 minimum sizes. |

A field the model could not find goes to **Needs review** ("Check by eye"), not **Mismatch**, because the model may have failed to read it. A missing government warning is always a **Mismatch**, and the bold and type-size rows are then left out.

### Batch processing

The server reads the CSV ([BatchCsv.cs](src/LabelVerify/BatchCsv.cs)). The browser then sends one label per request, 3 at a time ([Batch.cshtml](src/LabelVerify/Pages/Batch.cshtml)). No request is held open for a whole batch, and progress shows as each label finishes. If Azure OpenAI answers "too many requests", that label waits 20 seconds and tries again, up to 3 times.

## Tools used

- ASP.NET Core Razor Pages on .NET 10 (LTS)
- Azure OpenAI, `gpt-4.1-mini`, through the `Azure.AI.OpenAI` package
- xUnit for tests
- Azure App Service (Linux) for hosting
- Python with Pillow to draw the sample labels

## Assumptions

- The stack is Azure and .NET because the Compliance Division already runs on Azure and COLA is built on .NET. [Assumption] An Azure OpenAI endpoint is more likely than other AI services to be allowed through their firewall. This has not been confirmed with their IT team.
- Agents type the application fields in, or upload them as CSV. There is no connection to COLA, as the brief asks.
- Distilled spirits are the main case. Wine and beer rules (for example, alcohol content exceptions) are not handled separately.
- Nothing is stored. Images and results exist only for the length of the request and in the agent's browser.

## Trade-offs and limitations

- **Bold detection is a hint, not a ruling.** On 10 identical copies of a label whose whole warning is bold, the model called the body "not bold" once, so 1 in 10 showed **Match** instead of **Needs review**. Real photos will likely do worse. Agents should confirm bold type by eye. On the real bottle, whose warning uses a heavy condensed typeface throughout, the model called the body bold in 5 of 7 runs, sending it to **Needs review**.
- **Hard-to-read warnings.** Before the readability check was added, a photo with glare over the warning came back as an exact match, so the model can fill in text it cannot fully see. With the check, that photo goes to **Needs review** (2 of 2 runs). The model still decides for itself whether the warning is readable, so agents should look at the warning on any poor photo.
- **Warning type size is a judgment, not a measurement.** Across all runs, the tiny-warning sample was flagged 14 of 16 times; the 2 misses showed **Match**, a wrong pass. Normal labels were not flagged. The legal minimum sizes in 27 CFR 16.22 (for example 2 mm for containers over 237 mL) cannot be checked from a photo without knowing its scale.
- **Real photos need to be close up and upright.** On one real bottle (front and back photos, back label text running sideways and small), the bottler was wrong in 5 of 5 runs as photographed. With the back photo turned upright and cropped to the label using the tools on the single-label page, it was right in 10 of 10 runs, and the total wait fell from 4.8–6.6 seconds to 3.1–3.6 seconds. The other samples are drawn labels and simulated photos (tilt, glare, dim light, blur).
- **No automatic rotation or cropping.** Three automatic approaches were tried on the real photos and rejected: asking the model which way text runs (right only 6 of 12 times, and it called an upright photo sideways), sending every photo in all rotations (accurate, but 6–7 seconds per label), and trimming plain background automatically (it cut off part of the brand name). The batch page therefore cannot turn or crop; it tells agents to prepare photos first or use the single-label page for hard labels.
- **The model can invent text it expects to see.** When it could not read the sideways bottler line on the real bottle, it reported the brand's own company name and a ZIP code that are not on the label, and prompt instructions against outside knowledge did not stop it. When a since-fixed bug sent one image as a single pixel, it invented a street address and a country. The rules catch most of this as a mismatch against the application, but agents should treat the "Label says" column as the model's reading, not proof.
- **Several images per label.** Front and back were combined correctly in all 36 test runs (single-label page, live site, and a 10-label batch). The prompt also tells the model that the bottler and warning are often on the back label.
- **Batch speed is limited by the Azure OpenAI quota.** Each check uses about 2,000 tokens (about 2,500 for front and back). The deployment allows 200 requests and 200,000 tokens per minute, the most the free-trial quota allows for this model. Measured: 120 labels (10 of them front and back) in 1 minute 24 seconds with no rate-limit errors, so a 300-label batch takes about 3.5 to 4 minutes. Several agents running batches at the same time share that quota. If the limit is hit, a label waits 20 seconds and retries up to 3 times; this path has not been triggered in testing.
- **Speed.** Reading a label took 1.7 to 2.8 seconds in testing, including a 4032×3024 phone photo. Front and back together took 2.0 to 2.1 seconds. One real bottle photo (front only) took 4.3 seconds to read and 5.2 seconds in total, over the target, so real photos may be slower than the samples. The total wait seen in the browser was 2.7 to 4.0 seconds, under the 5-second target. A slow network or the first request after the app has been idle can add time.
- **A second real bottle (Aperol, an import).** Its warning body is printed all in capitals, which an earlier, stricter rule wrongly rejected. The model also sometimes reported the product name "APERITIVO" as the class instead of "LIQUEUR", and gave the Italian producer together with the importer as the bottler. After the warning rule change, a clearer class/type prompt and the bottler rule above, brand, class, alcohol, net contents, bottler, country and warning wording all matched in 5 of 5 runs; only bold type went to **Needs review**, because the warning uses a heavy condensed typeface. Reading took 3.5 to 4.5 seconds for the uncropped front and back photos.
- **A third real bottle (Bluemont Vineyard Chambourcin, a Virginia wine)** first showed three false **Mismatch** results: the model took the large grape name "CHAMBOURCIN" as the brand, the short application bottler ("Bluemont Vineyard, Bluemont, VA") did not appear as one phrase because a street line sits between, and the warning lost one comma in condensed small print. After the brand and bottler prompt changes, the word-order bottler rule and the punctuation rule, 5 of 5 runs had no **Mismatch**: the bottler matched every time, the brand matched 3 of 5 times (twice read as just "BLUEMONT", sent to **Needs review**), and the warning went to **Needs review** naming the missing comma. Wine-only rules, such as the sulfite statement and the appellation required with a vintage, are not checked.
- **The unreadable flag catches missing text, not confident guesses.** It fired on a label with the alcohol content and net contents washed out by glare (3 of 3 runs) and never on good photos (0 of 9 real-bottle runs, 11 of 11 samples unchanged). But on the real bottle with sideways back-label text, the model invented a bottler and a country ("USA") that are not on the label and did not flag either (0 of 3). The model does not seem to know when it is inventing; turning and cropping the photo remains the fix for that case.
- **Class/type on the Aperol bottle still varies.** The label shows both "APERITIVO" and "LIQUEUR"; the model reported "APERITIVO" in about 4 of 13 recent runs, a **Mismatch** against "Liqueur". An earlier 3 of 3 result after a prompt change did not hold up over more runs.
- **Prompt changes were tuned on three real bottles.** Each change was rechecked against all three bottles and the 11 samples, but three bottles is a small set; other labels may expose other reading habits.
- **Cropping needs a mouse or touch screen.** The turn buttons work from the keyboard, but drawing a crop box does not. Cropping is optional, so keyboard-only users can still check labels; they get the full photo read as uploaded.
- **No sign-in.** Anyone with the link can use the prototype. A production version would need sign-in, audit logging and a records retention policy.
- **File types.** JPG, PNG, WebP and GIF only. iPhone HEIC photos must be saved as JPG first.
- **Model lifetime.** Azure lists `gpt-4.1-mini` as Legacy, retiring 2027-04-14. Moving to a newer model means changing the deployment, not the code. The newer `gpt-5.4-mini` had no quota on the free-trial subscription used here.

## Project layout

```
src/LabelVerify/
  LabelReader.cs      One Azure OpenAI call: image in, label fields out
  LabelCheck.cs       Field-by-field comparison rules
  LabelChecker.cs     File checks, error messages and timing, shared by both pages
  BatchCsv.cs         Reads the batch CSV
  Pages/              Single-label page, batch page, shared layout
tests/LabelVerify.Tests/   Unit tests for the rules and the CSV reader
scripts/smoke_test.py      Runs all samples against a running app and checks the expected results
samples/                   Test labels, simulated photos, sample CSV, and the scripts that draw them
docs/assignment.md         The original assignment brief
```

## Troubleshooting

| Problem | Fix |
|---|---|
| App stops at start with `Missing setting AzureOpenAI:...` | Set the three settings with user-secrets (locally) or App Service settings (deployed). |
| "The label reader is busy" | The Azure OpenAI rate limit was reached. Wait a minute and try again. |
| "The label could not be read" | The image may be damaged or not a real image. Try another photo, or check that label by eye. |
| "That file type is not supported" | Save the image as JPG or PNG. |
| Batch says "This image has no row in the CSV" | The image's file name must appear in the CSV `file` column. Names are not case-sensitive. |
