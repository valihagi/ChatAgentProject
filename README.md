# Label Chat Agent

Chat agent that turns natural-language product and packaging information into print-ready beverage labels using the [TEC-IT Barcode API](https://barcode.tec-it.com). Home task for TEC-IT (.NET full-stack).

You describe a product in the chat (German or English, incomplete or contradictory input is fine). The agent asks follow-up questions, validates the data (GTIN/SSCC check digits, barcode type vs. packaging level, dates) and shows the finished label with a barcode in the chat, ready to print or download.

## Stack
- Backend: ASP.NET Core (.NET 10) minimal API, `src/ChatAgent.Api`
- Frontend: plain HTML/CSS/JS in `src/ChatAgent.Api/wwwroot` (served by the API, no build step, English/German UI)
- LLM: Google Gemini (free tier, default model `gemini-3.5-flash`) behind the `IChatModel` interface; an offline mock is the default
- Tests: xUnit, `tests/ChatAgent.Tests`

## Setup for a new user

### 1. Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` should print 10.0.x; `global.json` accepts any 10.0.x from 10.0.100)
- A modern browser
- Git

```bash
git clone <repository-url>
cd ChatAgentProject
```

### 2. Get the credentials
| Credential | Needed for | Where to get it |
|---|---|---|
| `TECIT_ACCESS_ID` | Always (creates the barcode images) | Access id for the TEC-IT Barcode API, provided by TEC-IT |
| `GEMINI_API_KEY` | Only with `Chat__Provider=Gemini` | Free key from [Google AI Studio](https://aistudio.google.com/apikey) |

Never commit these values. `.gitignore` excludes `.env` files, but the safest options are the two below, which keep the values outside the repository.

### 3. Provide the credentials (choose one)

**Option A: environment variables.** Set them in the same terminal you start the app from.

macOS / Linux (bash, zsh):
```bash
export TECIT_ACCESS_ID="your-access-id"
export GEMINI_API_KEY="your-gemini-key"      # only for the Gemini provider
```

Windows PowerShell:
```powershell
$env:TECIT_ACCESS_ID = "your-access-id"
$env:GEMINI_API_KEY = "your-gemini-key"      # only for the Gemini provider
```

Windows cmd:
```bat
set TECIT_ACCESS_ID=your-access-id
set GEMINI_API_KEY=your-gemini-key
```

These last for the current terminal session only. To keep them, add the `export` lines to `~/.zshrc` / `~/.bashrc`, or use `setx TECIT_ACCESS_ID "..."` on Windows (then open a new terminal).

**Option B: .NET user-secrets** (stored outside the repository in your user profile, loaded automatically by `dotnet run`):
```bash
dotnet user-secrets set TECIT_ACCESS_ID "your-access-id" --project src/ChatAgent.Api
dotnet user-secrets set GEMINI_API_KEY "your-gemini-key" --project src/ChatAgent.Api   # only for the Gemini provider
dotnet user-secrets list --project src/ChatAgent.Api                                     # check (shows the values)
```
Environment variables take precedence over user-secrets. User-secrets are only loaded in the Development environment, which `dotnet run` uses by default.

### 4. Run
Offline mock LLM (default, no Gemini key needed, uses no quota; the mock only asks for a GTIN and then builds an EAN-13/EAN-14 label):
```bash
dotnet run --project src/ChatAgent.Api
```

With the real Gemini model:
```bash
# macOS / Linux
Chat__Provider=Gemini dotnet run --project src/ChatAgent.Api
# Windows PowerShell
$env:Chat__Provider = "Gemini"; dotnet run --project src/ChatAgent.Api
```
Open http://localhost:5080. Use the language selector in the header to switch between English and German (the browser language is used at first).

Example inputs (the mock understands only the first one; the others need `Chat__Provider=Gemini`):
- `0,5 l Apfelsaft naturtrüb, Flasche, GTIN 4006381333931`
- `Riesling Qualitätswein 0,75 l, 12,5 % vol, Flasche, GTIN 4006381333931`
- `Karton mit 12 Flaschen Apfelsaft, GTIN 14006381333938, GS1-128, Charge LOT42, MHD 2027-03-31`
- `Palettenetikett für Cola, EAN13 5449000000996, Charge L17` (contradictory: the agent will ask what is needed)
- `Apfelsaft 1 l mit 12 % vol` (contradictory: a non-alcoholic product with alcohol content)

### 5. Run the tests
```bash
dotnet test
```
No test uses the network or your Gemini quota (fake HTTP handlers and the mock LLM).

### Troubleshooting
| Symptom | Cause / fix |
|---|---|
| Startup error `TECIT_ACCESS_ID is not set` | Set it as in step 3. Variables exported in a terminal are not visible to apps started from an IDE or the desktop; start `dotnet run` from that terminal or use user-secrets. |
| Startup error `Chat:Provider is Gemini but GEMINI_API_KEY is not set` | Set the key, or run without `Chat__Provider=Gemini` to use the mock. |
| Startup error `Unknown Chat:Provider` | Valid values are `Mock` and `Gemini` (case-insensitive). |
| Chat shows "language model is unavailable ... 503" | Gemini free-tier models are sometimes overloaded. Retry in a moment or try another model with `Gemini__Model=<model>`. |
| Chat shows "... 429 ... quota" | Free tier is only about 20 requests per model per day. Wait, or switch model with `Gemini__Model`. |
| Chat shows "barcode service could not create the label" | The Barcode API rejected the request or its per-IP rate limit was hit; wait a minute and retry. |
| `dotnet` not found | Install the .NET 10 SDK and open a new terminal (on macOS the default install path is `/usr/local/share/dotnet`). |

## Configuration reference
| Variable / setting | Default | Purpose |
|---|---|---|
| `TECIT_ACCESS_ID` | (required) | TEC-IT Barcode API access id |
| `GEMINI_API_KEY` | (required for Gemini) | Gemini API key |
| `Chat__Provider` | `Mock` | `Mock` or `Gemini` (case-insensitive; unknown values fail at startup) |
| `Gemini__Model` | `gemini-3.5-flash` | Gemini model name |
| `RateLimit__ChatPerMinute` | `12` | Chat turns per minute per client IP |
| `ASPNETCORE_URLS` | `http://localhost:5080` (launch profile) | Listen address |

Request limits: at most 40 messages per conversation, 2000 characters per message, 100 kB body.

## Architecture
```
Browser (wwwroot) -- POST /api/chat {messages, label} --> LabelAgent
   LabelAgent: IChatModel (Gemini | Mock) -> JSON {message, status, issues, label, cleared}
            -> LabelValidator (check digits, symbology fit, dates; builds barcode data)
            -> BarcodeClient (TEC-IT API) -> PNG as data URL
```
- The LLM extracts facts, detects gaps and contradictions and asks questions; deterministic code validates and builds the barcode data string, so the model never computes check digits.
- The server is stateless: the browser sends the conversation plus the last label specification. The model's `label` is applied as a patch onto that state (weaker models sometimes drop known fields); fields are only removed via an explicit `cleared` list.
- If the LLM says "ready" but validation fails, the findings go back to the LLM once so it can phrase the question in the user's language. That second answer is never rendered, because a value it "fixed" would not have been confirmed by the user.
- Label content rules (`LabelValidator.CheckContent`, deliberately simplified assumptions, **not legal advice**):
  - Net volume is required on consumer-unit labels (optional on case and pallet labels), must be a number with `ml`, `cl` or `l`, and is normalized (`0.75L` becomes `0.75 l`). Numerals such as `1.000 ml` are rejected as ambiguous instead of guessed.
  - Alcohol content (`alcoholPercent`, % vol) has at most one decimal place and lies between 0 and 100. The model sets `alcoholic` (beer, wine, spirits: true; juice, water: false). Alcoholic products must state the value (the EU requires it above 1.2 % vol), and a non-alcoholic product with more than 1.2 % vol is reported as a contradiction.
  - Both are printed on the label card, with the decimal separator of the UI language.
- The system prompt is in `src/ChatAgent.Api/Prompts/system-prompt.md`; the supported barcode types are in `Barcode/BarcodeTypes.cs`.

## Logging
Standard ASP.NET Core console logging. The app logs turn outcomes (status, issue count), the names of failed validator fields, Gemini and Barcode API latency and failures. It never logs user text, field values or credentials. Adjust with the usual `Logging__LogLevel__*` settings.

## Barcode API notes (observed)
- Requests are sent as POST so the access id never appears in a URL.
- Errors are returned as HTTP 200 with an `image/gif` error bitmap (`onerror=500` is not honoured), so the client treats any media type different from the requested one as a failure.
- This access id behaves like a non-subscriber: max 300 DPI, no SVG, per-IP rate limit.
- Size check: when the user requests a label size, the backend first renders the symbol at the smallest acceptable bar width, reads the real width from the PNG and reports a conflict if the requested area is smaller (an estimate from the symbol structure was off by up to 40 % against measurements).
- Sizing: `unit=fit` with `width`/`height` in mm scales the whole symbol into the box; `unit=mm` *crops* it at the canvas edge, so it must not be used for fixed label sizes. A box that is too small for the data yields a scaled-down, possibly unscannable symbol (no warning from the API).
- Default label size: the backend sets a module width in mm per symbology (EAN/UPC 0.33, GS1-128/EAN-14/Code 128 0.25, 2D 0.5) so sizes are deterministic. Without it the API picks its own scale (a long GS1-128 came out ~240 mm wide).
- The API does not validate GS1 check digits (a wrong GTIN check digit in GS1-128 still renders), so the backend validates them.

## Gemini notes (observed)
- The free tier allows only about **20 requests per model per day** (`generate_content_free_tier_requests`), and each model has its own quota. A chat turn costs 1 request (2 if validation feedback is needed); retried 503s probably count too. A 429 is not retried.
- Free-tier models are intermittently overloaded (503). The client retries a 503 once. `gemini-3.7/3.8-flash` were overloaded for long stretches; `gemini-3.5-flash` was reliable and is the default.
- Verified against the real API (`gemini-3.5-flash`, `gemini-3.7-flash` and `gemini-3.5-flash-lite`): vague German input leads to a follow-up question; contradictory pallet/EAN-13/past-date input leads to all conflicts being named; a complete case label renders GS1-128; a wrong check digit is caught; a follow-up edit to a Digital Link QR code keeps earlier fields; a 12-digit GTIN is completed; a relative date ("Ende nächsten Monats") is resolved from the date the backend injects; the past-date flow works end to end (question, user confirmation, `allowPastDate`, label rendered).
- Lesson: with a `responseSchema`, keys that are not `required` are silently omitted by the model (a first turn returned only the product name although GTIN, date and count had been given). All label keys are therefore required (nullable) and carry short descriptions.
- Verified live on `gemini-3.5-flash-lite` (2026-09-29, transcripts in `docs/samples/`): extraction of net volume and alcohol content (`alcoholic`, `alcoholPercent`); a 12 % vol apple juice is flagged as a contradiction; a relative date ("Ende nächsten Monats") is resolved and used without a needless confirmation question; the size warning fires with the API's measured width ("at least 114 mm" for a 60 mm request); withdrawing a value ("Doch keine Charge") removes only that value (`cleared`).
- Observed weaknesses of the small model: it sometimes asks for data that is not required (a best-before date for a consumer unit in GS1-128) and it classified the 12 % apple juice as `alcoholic: true`, so the contradiction was caught by the model's own reasoning and not by the validator's rule (which needs `alcoholic: false`). The rules depend on the model's classification.
- `gemini-3.6-flash` and `gemini-3.8-flash` answered 503 (overloaded) whenever tried.

## Open points (label content not covered yet)
The task asks for "konforme" labels and provides no rule packs, so the scope was decided explicitly: **barcode correctness plus net volume and alcohol content**. Still open:
- Allergen declaration (e.g. sulphites in wine) and ingredient list / nutrition table for soft drinks
- Producer or bottler name and address
- Deposit mark (Pfand) and recycling symbols
- Country of origin, lot/date marking rules per market, and legally prescribed pack sizes
- Market-specific variants (EU vs. US/UPC-A) beyond choosing the barcode type
- Layout and typography rules (minimum font sizes, e-mark) and a fixed label template with several barcodes

## Known limitations
- Output is a 300 DPI PNG (limit of the access id, no SVG/vector), so very large print sizes are not crisp.
- "Print-ready" covers the barcode with its data, the product name, net volume and alcohol content (see the rules above). Further regulatory label content is **not modelled** (open points below).
- One label per conversation state; several labels (bottle, case, pallet) need separate chats.
- GS1 Digital Link codes point to GS1's generic resolver (`id.gs1.org`), which only resolves GTINs registered there.
- Barcodes were not verified with a scanner. The size check compares the requested area with the width the API reports at the smallest bar width (approximated GS1 minimums); it costs one extra Barcode API call when a size is requested.
- Server-side messages (errors, the validator's fallback text) are English; the interface and the check-digit notice are translated, and the model answers in the user's language.
- The conversation is not persisted; reloading the page starts a new chat.
