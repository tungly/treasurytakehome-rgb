# Label Check

Label Check compares an alcohol label image with what the label application says. It reads the label with an AI vision model, then checks each field with plain, testable rules and shows **Match**, **Needs review**, or **Mismatch** for each one. Agents can check one label or a whole batch.

**Live prototype:** https://labelverify-16883.azurewebsites.net

The assignment brief is in [docs/assignment.md](docs/assignment.md).

## Using the app

### Check one label

1. Open the app. **Check one label** is the first page.
2. Choose the label image (JPG or PNG, up to 10 MB).
3. Type in what the application says: brand name, class/type, alcohol content, net contents, and bottler name and address. Fill in the country of origin only for imports.
4. Press **Check label**. The result usually appears in about 2 seconds.

The government warning is always checked against the required wording, so there is nothing to type for it.

### Check a batch

1. Open **Check a batch**.
2. Choose all the label images at once.
3. Choose a CSV file with one row per label. Use **Download a CSV template** on the page, or see [samples/applications.csv](samples/applications.csv).
4. Press **Check all labels**. Results fill in as each label finishes. Use **Show only labels that need attention** to hide the labels that match, and **Download results (CSV)** to save them.

CSV columns: `file`, `brand_name`, `class_type`, `alcohol_content`, `net_contents`, `bottler`, `country_of_origin`. The `file` column must match the image's file name. Leave `country_of_origin` blank for domestic products.

### Try it with the sample files

The [samples](samples/) folder has 9 test labels and a matching CSV. Upload all 9 images and `applications.csv` on the batch page.

| Sample | Expected result |
|---|---|
| `old-tom-good.png` | Everything matches |
| `old-tom-title-case-warning.png` | Mismatch: "Government Warning" is not in capitals |
| `stones-throw-wrong-abv.png` | Mismatch: alcohol content is 40%, application says 45%. Brand "STONE'S THROW" still matches "Stone's Throw" |
| `highland-import-bold-body.png` | Needs review: the whole warning is bold, but only the header may be |
| `photo-old-tom-*.jpg` | The good label (and one title-case label) as rough phone photos: tilted, glare, dim and blurry |

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

Recreate the sample images (needs Python with Pillow and macOS Arial fonts):

```bash
python3 samples/make_labels.py && python3 samples/make_photos.py
```

### Create the Azure OpenAI resource

```bash
az group create --name label-verify --location eastus2
az cognitiveservices account create --name <resource-name> --resource-group label-verify --location eastus2 --kind OpenAI --sku S0 --custom-domain <resource-name> --yes
az cognitiveservices account deployment create --name <resource-name> --resource-group label-verify --deployment-name label-reader --model-name gpt-4.1-mini --model-version 2025-04-14 --model-format OpenAI --sku-name GlobalStandard --sku-capacity 100
```

If `account create` fails with `MissingSubscriptionRegistration`, run `az provider register --namespace Microsoft.CognitiveServices --wait` first.

## Deploying to Azure App Service

```bash
az provider register --namespace Microsoft.Web --wait
az appservice plan create --name label-verify-plan --resource-group label-verify --location centralus --is-linux --sku B1
az webapp create --name <app-name> --resource-group label-verify --plan label-verify-plan --runtime "DOTNETCORE:10.0" --https-only true
az webapp config appsettings set --name <app-name> --resource-group label-verify --output none --settings \
  AzureOpenAI__Endpoint="https://<resource-name>.openai.azure.com/" AzureOpenAI__Deployment=label-reader \
  AzureOpenAI__Key="$(az cognitiveservices account keys list --name <resource-name> --resource-group label-verify --query key1 -o tsv)"
dotnet publish src/LabelVerify -c Release -o ./publish && (cd publish && zip -qr ../app.zip .)
az webapp deploy --name <app-name> --resource-group label-verify --src-path app.zip --type zip
```

App Service reads the settings as environment variables. The double underscore (`__`) stands for the `:` in the setting names.

The plan is in Central US because the free-trial subscription had no B1 quota in East US or East US 2. If `plan create` fails with "Operation cannot be completed without additional quota", try another region. The app and the Azure OpenAI resource do not need to be in the same region.

## How it works

1. **Read.** The image goes to Azure OpenAI in one call ([LabelReader.cs](src/LabelVerify/LabelReader.cs)). The model must answer in a fixed JSON shape, at temperature 0, and is told to copy text exactly as printed, without fixing typos or capital letters.
2. **Compare.** Plain C# code compares each field with the application ([LabelCheck.cs](src/LabelVerify/LabelCheck.cs)). The model never decides pass or fail, so every rule is unit tested and easy to explain.
3. **Show.** The page shows each field side by side with its result and a short reason.

### Rules for each field

| Field | Rule |
|---|---|
| Brand, class/type, bottler, country | Ignores case, punctuation and spacing, so "STONE'S THROW" matches "Stone's Throw". A near miss (a typo, or one value inside the other) goes to **Needs review**. |
| Alcohol content | Compares the percentages. If the label shows proof, it must be twice the ABV. |
| Net contents | Converts both to mL first, so "750 mL" matches "75 cL" and "12 fl oz" matches "355 mL" (within 0.5%). |
| Government warning wording | Must match 27 CFR 16.21 word for word, including "GOVERNMENT WARNING:" in capitals. Only line breaks and extra spaces are forgiven. A mismatch names the first different word. |
| Government warning bold type | "GOVERNMENT WARNING:" must be bold and the rest must not be (27 CFR 16.22). Problems go to **Needs review**, because bold is judged from the image. |

A field the model could not find goes to **Needs review** ("Check by eye"), not **Mismatch**, because the model may have failed to read it. A missing government warning is always a **Mismatch**.

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

- **Bold detection is a hint, not a ruling.** On 10 identical copies of a label whose whole warning is bold, the model called the body "not bold" once, so 1 in 10 showed **Match** instead of **Needs review**. Real photos will likely do worse. Agents should confirm bold type by eye.
- **Hard-to-read warnings.** In testing, the model copied a one-word change in the warning correctly, even under glare. A photo with glare over the warning still came back as an exact match, though, so the model may fill in text it cannot fully see. Agents should confirm the warning by eye on poor photos.
- **Test images are synthetic.** The samples are drawn labels and simulated photos (tilt, glare, dim light, blur). No real bottle photos were tested.
- **Batch speed is limited by the Azure OpenAI quota.** The deployment allows 100 requests per minute. Measured: 40 labels in 40 seconds, so a 300-label batch takes about 5 minutes. Several agents running batches at the same time share that quota.
- **Speed.** Single checks took 1.7 to 2.5 seconds in testing, under the 5-second target. A first request after the app has been idle can be slower.
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
