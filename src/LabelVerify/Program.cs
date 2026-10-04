using System.ClientModel;
using Azure.AI.OpenAI;
using LabelVerify;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

var ai = builder.Configuration.GetSection("AzureOpenAI");
string Setting(string name) => ai[name] is { Length: > 0 } value
    ? value
    : throw new InvalidOperationException($"Missing setting AzureOpenAI:{name}. See README for setup.");

var client = new AzureOpenAIClient(
    new Uri(Setting("Endpoint")),
    new ApiKeyCredential(Setting("Key")),
    new AzureOpenAIClientOptions { NetworkTimeout = TimeSpan.FromSeconds(30) });
builder.Services.AddSingleton(new LabelReader(client.GetChatClient(Setting("Deployment"))));
builder.Services.AddSingleton<LabelChecker>();

var app = builder.Build();
app.MapRazorPages();
app.Run();
