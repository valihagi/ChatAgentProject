# Label Chat Agent

Chat agent that turns natural-language product and packaging information into print-ready beverage labels using the [TEC-IT Barcode API](https://barcode.tec-it.com). Home task for TEC-IT (.NET full-stack).

You describe a product in the chat (German or English; incomplete or contradictory input is fine). The agent asks follow-up questions, validates the data (GTIN/SSCC check digits, barcode type vs. packaging level, dates, net volume, alcohol content) and shows the finished label with its barcode in the chat, ready to print or download.

**Contents:** [Quick start](#quick-start) · [Setup](#setup-for-a-new-user) · [Using the app](#using-the-app) · [Configuration](#configuration-reference) · [Architecture](#architecture) · [Design decisions](#design-decisions-and-assumptions) · [Label rules](#label-rules) · [Barcode API notes](#barcode-api-notes-observed) · [Gemini notes](#gemini-notes-observed) · [Tests](#tests) · [Open points](#open-points-label-content-not-covered-yet) · [Limitations](#known-limitations) · [Development process](#development-process)

## Quick start
```bash
git clone <repository-url> && cd ChatAgentProject
dotnet user-secrets set TECIT_ACCESS_ID "<id>"   --project src/ChatAgent.Api
dotnet user-secrets set GEMINI_API_KEY  "<key>"  --project src/ChatAgent.Api
dotnet run --project src/ChatAgent.Api            # http://localhost:5080 (add Chat__Provider=Mock to run offline)
```
Details, other operating systems and troubleshooting are below.

## Stack
- Backend: ASP.NET Core (.NET 10) minimal API, `src/ChatAgent.Api`
- Frontend: plain HTML/CSS/JavaScript in `src/ChatAgent.Api/wwwroot` (served by the API, no build step, no npm; English/German UI)
- LLM: Google Gemini (free tier), default model `gemini-3.5-flash-lite`, behind the `IChatModel` interface. An offline mock (`Chat__Provider=Mock`) speaks the same protocol
- Barcode generation: TEC-IT Barcode API (`https://barcode.tec-it.com/barcode.ashx`)
- Tests: xUnit, `tests/ChatAgent.Tests`

```
src/ChatAgent.Api
  Program.cs             composition root, endpoint, rate limiting, error handler
  Agent/                 LabelAgent (turn orchestration), LabelValidator (rules), LabelSpec, NetVolume, Gs1
  Chat/                  IChatModel, GeminiChatModel, MockChatModel, request limits, provider registration
  Barcode/               BarcodeClient (TEC-IT), BarcodeRequest, BarcodeTypes, PngInfo
  Prompts/system-prompt.md   the agent's system prompt
  wwwroot/               index.html, app.js (chat UI), i18n.js (EN/DE), label-image.js (label PNG), style.css
tests/ChatAgent.Tests    unit and HTTP endpoint tests
docs/Dokumentation_Label_Chat_Agent.pdf   German documentation (scope, decisions, screenshots, chats, limitations, time)
docs/session-log         scrubbed Claude Code session log: PDF (long outputs shortened) and complete Markdown
docs/dokumentation.html  source of the PDF; docs/screenshots and docs/samples hold its images and live transcripts
```

## Setup for a new user

### 1. Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` should print 10.0.x; `global.json` accepts any 10.0.x from 10.0.100)
- A modern browser (uses `AbortSignal.any`, canvas and `toLocaleString`; current Chrome, Edge, Firefox, Safari)
- Git

```bash
git clone <repository-url>
cd ChatAgentProject
```

### 2. Get the credentials
| Credential | Needed for | Where to get it |
|---|---|---|
| `TECIT_ACCESS_ID` | Always (creates the barcode images) | Access id for the TEC-IT Barcode API, provided by TEC-IT |
| `GEMINI_API_KEY` | Unless you run with `Chat__Provider=Mock` | Free key from [Google AI Studio](https://aistudio.google.com/apikey) |

Never commit these values. `.gitignore` excludes `.env` files, but the safest options are the two below, which keep the values outside the repository.

### 3. Provide the credentials (choose one)

**Option A: environment variables.** Set them in the same terminal you start the app from.

macOS / Linux (bash, zsh):
```bash
export TECIT_ACCESS_ID="your-access-id"
export GEMINI_API_KEY="your-gemini-key"
```

Windows PowerShell:
```powershell
$env:TECIT_ACCESS_ID = "your-access-id"
$env:GEMINI_API_KEY = "your-gemini-key"
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
dotnet user-secrets set GEMINI_API_KEY "your-gemini-key" --project src/ChatAgent.Api
dotnet user-secrets list --project src/ChatAgent.Api      # check (this prints the values)
```
Environment variables take precedence over user-secrets. User-secrets are only loaded in the Development environment, which `dotnet run` uses by default.

### 4. Run
```bash
dotnet run --project src/ChatAgent.Api
```
Open http://localhost:5080 (the launch profile opens it automatically). The shipped default uses the real Gemini model (`appsettings.json`: `Chat:Provider=Gemini`).

Offline without a Gemini key or quota (the mock only asks for a GTIN and then builds an EAN-13/EAN-14 label):
```bash
# macOS / Linux
Chat__Provider=Mock dotnet run --project src/ChatAgent.Api
# Windows PowerShell
$env:Chat__Provider = "Mock"; dotnet run --project src/ChatAgent.Api
```

### 5. Run the tests
```bash
dotnet test
```
No test uses the network or your Gemini quota (fake HTTP handlers, the mock LLM, and an in-process test server that is forced to the mock provider).

### Troubleshooting
| Symptom | Cause / fix |
|---|---|
| Startup error `TECIT_ACCESS_ID is not set` | Set it as in step 3. Variables exported in a terminal are not visible to apps started from an IDE or the desktop; start `dotnet run` from that terminal or use user-secrets. |
| Startup error `Chat:Provider is Gemini but GEMINI_API_KEY is not set` | Set the key, or run with `Chat__Provider=Mock`. |
| Startup error `Unknown Chat:Provider` | Valid values are `Mock` and `Gemini` (case-insensitive). |
| Chat shows "language model is unavailable ... 503" | Gemini free-tier models are sometimes overloaded. Retry in a moment or try another model with `Gemini__Model=<model>`. |
| Chat shows "... 429 ... quota" | The free tier allows only about 20 requests per model per day. Wait, or switch model with `Gemini__Model`. |
| Chat shows "barcode service could not create the label" | The Barcode API rejected the request or its per-IP rate limit was hit; wait a minute and retry. |
| Chat shows "Too many requests" | This app limits turns per minute per IP (default 12, `RateLimit__ChatPerMinute`). |
| `dotnet` not found | Install the .NET 10 SDK and open a new terminal (on macOS the default install path is `/usr/local/share/dotnet`). |
| Port 5080 already in use | Stop the other instance, or set `ASPNETCORE_URLS=http://localhost:5090` and add `--no-launch-profile` (then also set `ASPNETCORE_ENVIRONMENT=Development` if you use user-secrets). |

## Using the app
Example inputs (the mock understands only the first one; the others need Gemini):
- `0,5 l Apfelsaft naturtrüb, Flasche, GTIN 4006381333931`
- `Riesling Qualitätswein 0,75 l, 12,5 % vol, Flasche, GTIN 4006381333931`
- `Karton mit 12 Flaschen Apfelsaft 0,75 l, GTIN 14006381333938, GS1-128, Charge LOT42, MHD Ende nächsten Monats`
- `... Etikettengröße 60 mm x 30 mm` (the agent warns if the data does not fit)
- `Palettenetikett für Cola, EAN13 5449000000996, Charge L17` (contradictory: the agent asks what is needed)
- `Apfelsaft 1 l mit 12 % vol` (contradictory: a non-alcoholic product with alcohol content)
- Follow-ups after a label: `Nimm lieber einen QR-Code`, `Doch keine Charge`, `Das MHD ist bewusst in der Vergangenheit`

Below a finished label: **Print label** prints only the label card. **Download label (PNG)** saves the complete label (product text, barcode at its true size, facts) as one 300 DPI PNG, drawn in the browser on a canvas with a resolution header so image viewers print it at the right physical size. **Barcode only** saves the raw image from the TEC-IT API. **New chat** resets the conversation; the header selector switches the interface between English and German (browser language by default, remembered).

## Configuration reference
| Variable / setting | Default | Purpose |
|---|---|---|
| `TECIT_ACCESS_ID` | (required) | TEC-IT Barcode API access id |
| `GEMINI_API_KEY` | (required for Gemini) | Gemini API key |
| `Chat__Provider` | `Gemini` (appsettings.json; `Mock` if unset) | `Mock` or `Gemini` (case-insensitive; unknown values fail at startup) |
| `Gemini__Model` | `gemini-3.5-flash-lite` | Gemini model name |
| `RateLimit__ChatPerMinute` | `12` | Chat turns per minute per client IP |
| `ASPNETCORE_URLS` | `http://localhost:5080` (launch profile) | Listen address |
| `Logging__LogLevel__*` | Information | Standard ASP.NET Core logging |

Fixed limits: at most 40 messages per conversation, 2000 characters per message, 100 kB request body. Timeouts: Gemini 45 s, Barcode API 20 s, browser request 90 s.

## Architecture
```
Browser (wwwroot) -- POST /api/chat {messages, label} --> LabelAgent
   LabelAgent: IChatModel (Gemini | Mock) -> JSON {message, status, issues, label, cleared}
            -> LabelValidator (check digits, symbology fit, dates, content rules; builds barcode data)
            -> [size probe] -> BarcodeClient (TEC-IT API) -> PNG as data URL
```

**API.** `POST /api/chat` with `{ "messages": [{ "role": "user" | "agent", "text": "..." }], "label": { ...last label spec, optional } }`. Answer: `{ "reply", "status": "needs_info" | "ready", "label", "image" (PNG data URL or null), "dpi", "notices": [] }`. Errors are always `{ "error": "..." }`: 400 invalid request, 429 rate limit, 502 Gemini or Barcode API problem, 500 unexpected. `GET /` serves the frontend.

**One turn.** The browser sends the whole conversation plus the last label specification. The model answers with JSON (constrained by a response schema): a message for the user, `needs_info` or `ready`, a list of issues (missing / conflict / invalid) and the label fields it knows. The backend merges that into the previous state, and when the model says `ready` it validates the label deterministically, builds the barcode data string, optionally checks the requested size, and renders the barcode. If the model says `ready` but validation disagrees, the findings go back to the model once so it can phrase the question in the user's language.

## Design decisions and assumptions
1. **The LLM extracts and asks; code validates and builds.** Language models are unreliable at check-digit arithmetic and GS1 syntax, and the Barcode API does not validate GS1 data (a wrong check digit still renders). So the model never writes barcode data: `LabelValidator` computes and verifies check digits, assembles the GS1 element string and rejects contradictions.
2. **Deterministic pipeline instead of a tool-calling agent.** A fixed loop (extract, validate, render, at most one feedback round) keeps behaviour predictable and cost bounded, which matters with a free tier of about 20 requests per model per day. A tool-calling variant (the model calls `validate` and `render` itself) is a possible next step.
3. **Stateless server, state in the browser.** The client sends the conversation and the last label; nothing is stored. The model's `label` is applied as a *patch* (null means unchanged, removals only via an explicit `cleared` list) because weaker models sometimes drop known fields.
4. **The feedback answer is never rendered.** If validation fails and the model "fixes" a value (for example swaps in the expected check digit), that value was never confirmed by the user, so only a question is accepted from that round and the label keeps the user's original value.
5. **Structured output.** Gemini runs with a JSON response schema in which every label key is required (nullable) and described. Without `required`, schema mode silently omitted fields the user had given (found in a live test). Temperature 0.2. Model output is normalized (casing, spaces in numbers, empty strings).
6. **The label is composed in the browser.** The Barcode API only draws barcodes, so the product text, facts and layout are ours (HTML card for screen and print, canvas PNG for download).
7. **Deterministic sizes.** Without a module width the API picks its own scale (a long GS1-128 came out ~240 mm wide). The backend sets one per symbology and, for an explicit size, uses `unit=fit`; `unit=mm` crops the symbol.
8. **Size feasibility by measurement.** Estimating the symbol width was off by up to 40 % against real renders, so the backend renders once at the smallest scannable bar width, reads the width from the PNG header and compares.
9. **Curated barcode types.** Only types whose data format the validator can build are allowed (subset of section 3 of the API reference). UPC-E and GS1 DataBar were left out on purpose.
10. **Explicit content scope.** "Konform" is interpreted as barcode/GS1 correctness plus net volume and alcohol content; everything else is listed as an open point.
11. **Secrets and configuration.** Secrets only via environment or user-secrets. The app fails at startup, with a hint, if a required credential or an unknown provider is configured. The access id is sent in a POST body, never in a URL.
12. **Abuse and quota protection.** Request size caps, role validation and a per-IP rate limit protect the upstream quotas. Only 503 is retried (once); 429 is a quota limit and retrying would burn more of it.
13. **Language.** The model answers in the user's language; the interface is translated (EN/DE); backend-generated facts such as "GTIN completed with check digit" travel as language-neutral notice codes.
14. **Digital Link assumption.** GS1 Digital Link codes use GS1's generic resolver (`https://id.gs1.org/01/{gtin14}[/10/{batch}][?15={yymmdd}]`) so no URL has to be collected.
15. **Plain JavaScript.** No framework or build step keeps the project small and runnable with only the .NET SDK.

## Label rules
**Barcode type per packaging level (defaults; the user may choose another allowed type)**
| Packaging level | Default | Notes |
|---|---|---|
| `consumer_unit` (bottle, can) | `EAN13` | `EAN8` for tiny packs, `UPCA` for US/CA, `GS1DigitalLink_QRCode` for a consumer-info code |
| `case` (carton, crate, tray) | `EAN14`, or `GS1-128` if batch, date or item count are needed | |
| `pallet` | `GS1-128` with the SSCC | GTIN, batch and date optional |

Allowed types: `EAN13`, `EAN8`, `UPCA`, `EAN14`, `GS1-128`, `Code128`, `Code39`, `QRCode`, `DataMatrix`, `GS1QRCode`, `GS1DataMatrix`, `GS1DigitalLink_QRCode`, `GS1DigitalLink_DataMatrix`.

**Checks (`LabelValidator`)**
| Area | Rule |
|---|---|
| Required | product name, packaging level, barcode type; SSCC on pallets; net volume on consumer units |
| GTIN / SSCC | digits only; correct length per type; check digit verified. EAN/UPC types accept the GTIN without check digit (computed and announced); GS1 codes need the complete number |
| Type vs. packaging | pallet needs a GS1 element-string code and an SSCC; EAN-14 is not for consumer units; item count only on cases, SSCC only on pallets |
| Attributes | batch (1-20 characters, restricted set), best-before date (`YYYY-MM-DD`, GS1 AI 15), item count (AI 37) only on codes that can carry them (GS1-128 / GS1 2D; Digital Link: batch and date) |
| Dates | must be real dates; a date in the past is a conflict unless the user confirmed it (`allowPastDate`) |
| Size | width and height together, 0-300 mm; the symbol must fit at the smallest scannable bar width |

**Content rules (deliberately simplified assumptions, not legal advice)**
- Net volume: number plus `ml`, `cl` or `l`, plausible (up to 100 l), canonicalized (`0.75L` becomes `0.75 l`). Numerals like `1.000 ml` are rejected as ambiguous instead of guessed.
- Alcohol content (`alcoholPercent`, % vol): at most one decimal, 0 to 100. The model sets `alcoholic` (beer, wine, spirits: true; juice, water: false). Alcoholic products must state the value (the EU requires it above 1.2 % vol); a non-alcoholic product with more than 1.2 % vol is a contradiction.

## Barcode API notes (observed)
- Requests are sent as POST so the access id never appears in a URL.
- Errors are returned as HTTP 200 with an `image/gif` error bitmap (`onerror=500` is not honoured), so the client treats any media type different from the requested one as a failure. The error text exists only inside the image.
- This access id behaves like a non-subscriber: max 300 DPI, no SVG, per-IP rate limit (reached after about ten rapid calls).
- `unit=fit` with `width`/`height` in mm scales the whole symbol into the box; `unit=mm` *crops* it at the canvas edge, so it must not be used for fixed label sizes.
- Default module widths (EAN/UPC 0.33 mm, GS1-128 / EAN-14 / Code 128 0.25 mm, 2D 0.5 mm) give deterministic sizes; EAN-13 at 0.33 mm is 37.3 mm wide, the GS1 nominal size.
- The size check costs one extra call when a size is requested.

## Gemini notes (observed)
- The free tier allows only about **20 requests per model per day** (`generate_content_free_tier_requests`); each model has its own quota. A chat turn costs 1 request (2 if validation feedback is needed); retried 503s probably count too.
- Free-tier models are intermittently overloaded (503). `gemini-3.6-flash`, `gemini-3.7-flash` and `gemini-3.8-flash` were overloaded for long stretches; `gemini-3.5-flash` and `gemini-3.5-flash-lite` were reliable. The default is `gemini-3.5-flash-lite` (it had quota left and works with the final schema); switch with `Gemini__Model`.
- Verified live on `gemini-3.5-flash-lite` with the final schema and prompt (transcripts in `docs/samples/`): the past-date flow end to end (question, confirmation, `allowPastDate`); a relative date ("Ende nächsten Monats") resolved from the date the backend injects and used without a needless confirmation; extraction of net volume and alcohol content; a 12 % vol apple juice flagged; the size warning with the API's measured width; withdrawing a value removes only that value.
- Verified live on other models, before the response schema and content rules existed (`gemini-3.5-flash`, `gemini-3.7-flash`), and **not repeated on the final configuration**: a vague German request leads to a follow-up question; contradictory pallet / EAN-13 / past-date input names all conflicts; a complete case label renders GS1-128; a wrong check digit is caught; a follow-up edit to a Digital Link QR code keeps earlier fields; a 12-digit GTIN is completed.
- Observed weaknesses of the small model: it sometimes asks for data that is not required (a best-before date on a consumer unit in GS1-128), and it classified the 12 % apple juice as `alcoholic: true`, so that contradiction was caught by the model's own reasoning, not by the validator (which needs `alcoholic: false`). The content rules depend on the model's classification.

## Tests
`dotnet test` runs 162 tests (xUnit), none touching the network or the Gemini quota:
- **Validator and value objects:** GTIN/SSCC check digits, every barcode family, packaging fit, dates, net volume, alcohol content, normalization, patch merge.
- **Agent:** ready / needs-info paths, the feedback round (including that a "fixed" value is never rendered), state handling, size probe, logging content (no user values).
- **Clients:** Gemini request shape (system prompt with date, schema with required keys, retry policy, timeouts, non-JSON errors), Barcode client (error bitmaps, network failures, access id in the body).
- **HTTP endpoint** (in-process server): request limits, rate limiting, error responses, frontend served.
- **Not covered by automated tests:** the JavaScript frontend (checked manually in the browser), real printing, and live Gemini behaviour (checked in live sessions, see above).

## Logging
Standard ASP.NET Core console logging. The app logs turn outcomes (status, issue count), the names of failed validator fields, render sizes, and Gemini and Barcode API latency and failures. It never logs user text, field values or credentials.

## Open points (label content not covered yet)
The task asks for "konforme" labels and provides no rule packs, so the scope was decided explicitly: **barcode correctness plus net volume and alcohol content**. Still open:
- Allergen declaration (e.g. sulphites in wine) and ingredient list / nutrition table for soft drinks
- Producer or bottler name and address
- Deposit mark (Pfand) and recycling symbols
- Country of origin, lot/date marking rules per market, and legally prescribed pack sizes
- Market-specific variants (EU vs. US/UPC-A) beyond choosing the barcode type
- Layout and typography rules (minimum font sizes, e-mark) and a fixed label template with several barcodes
- Next technical steps: several labels per conversation, a tool-calling agent variant, scanner-based verification, persistence and authentication

## Known limitations
- Output is a 300 DPI PNG (limit of the access id, no SVG/vector), so very large print sizes are not crisp. Printing from the browser is only true to size if the print dialog scale is 100 %.
- "Print-ready" covers the barcode with its data, product name, net volume, alcohol content and key facts. Further regulatory content is not modelled (see open points).
- One label per conversation state; several labels (bottle, case, pallet) need separate chats.
- GS1 Digital Link codes point to GS1's generic resolver (`id.gs1.org`), which only resolves GTINs registered there.
- Barcodes were not verified with a scanner. The size check uses approximated GS1 minimum bar widths.
- The content rules that rely on the model's classification (`alcoholic`) can be bypassed by a misclassification.
- The free Gemini tier is small (about 20 requests per model per day) and sometimes overloaded; the app reports this but cannot work around it.
- Server-side messages (errors, the validator's fallback text) are English; the interface and the check-digit notice are translated, and the model answers in the user's language.
- The conversation is not persisted (reloading starts a new chat); there is no authentication or HTTPS; the app is meant to run locally.

## Development process
Built with Claude Code (Claude Sonnet 5.5) in a single session; the scrubbed session log is in `docs/session-log/` and keeps the failed attempts (for example the invalid first API probe, the retired Gemini model name, the cropping `unit=mm`, and the schema that dropped fields). The system prompt is part of the repository (`src/ChatAgent.Api/Prompts/system-prompt.md`). The git history documents the steps in small commits.
