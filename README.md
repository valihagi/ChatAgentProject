# Label Chat Agent

Chat agent that turns natural-language product/packaging info into print-ready beverage labels using the TEC-IT Barcode API. Home task for TEC-IT (.NET full-stack).

## Stack
- Backend: ASP.NET Core (.NET 10) minimal API, `src/ChatAgent.Api`
- Frontend: plain HTML/CSS/JS in `src/ChatAgent.Api/wwwroot` (served by the API, no build step)
- LLM: Google Gemini `gemini-3.5-flash` (free tier), behind the `IChatModel` interface; a mock is the default

## Requirements
.NET 10 SDK (`global.json` accepts any 10.0.x from 10.0.100).

## Run
```bash
dotnet run --project src/ChatAgent.Api      # http://localhost:5080
```

## Configuration
Secrets come from environment variables only and are never committed.

| Variable / setting | Purpose |
|---|---|
| `Chat__Provider` | `Mock` (default) or `Gemini` (case-insensitive; unknown values fail at startup) |
| `GEMINI_API_KEY` | Gemini API key (only with `Gemini`); store with `dotnet user-secrets` |
| `Gemini__Model` | Optional model override |
| `TECIT_ACCESS_ID` | TEC-IT Barcode API access id |

## Barcode API notes (observed)
- Requests are sent as POST so the access id never appears in a URL.
- Errors are returned as HTTP 200 with an `image/gif` error bitmap (`onerror=500` is not honoured), so the client treats any media type different from the requested one as a failure.
- This access id behaves like a non-subscriber: max 300 DPI, no SVG, per-IP rate limit.
- Sizing: `unit=fit` with `width`/`height` in mm scales the whole symbol into the box; `unit=mm` *crops* it at the canvas edge, so it must not be used for fixed label sizes. A box that is too small for the data yields a scaled-down, possibly unscannable symbol (no warning from the API).
- Default label size: the backend sets a module width in mm per symbology (EAN/UPC 0.33, GS1-128/EAN-14/Code 128 0.25, 2D 0.5) so sizes are deterministic. Without it the API picks its own scale (a long GS1-128 came out ~240 mm wide).
- The API does not validate GS1 check digits (a wrong GTIN check digit in GS1-128 still renders), so the backend must validate them.

## Gemini notes (observed)
- Free-tier models are intermittently overloaded (503). The client retries 503/429 twice; newer models (`gemini-3.7/3.8-flash`) were overloaded for long stretches, `gemini-3.5-flash` was reliable and is the default. Override with `Gemini__Model`.
- Live scenarios run against the real API: vague German input -> follow-up question; contradictory pallet/EAN13/past-date input -> all conflicts named; complete case label -> GS1-128 rendered; wrong check digit -> corrected digit suggested; follow-up edit to a Digital Link QR code keeps earlier fields.

## Tests
```bash
dotnet test
```
No test touches the network or the Gemini quota (fake HTTP handlers).

## Architecture
```
Browser (wwwroot) -- POST /api/chat {messages, label} --> LabelAgent
   LabelAgent: IChatModel (Gemini | Mock) -> JSON {message, status, issues, label}
            -> LabelValidator (check digits, symbology fit, dates; builds barcode data)
            -> BarcodeClient (TEC-IT API) -> PNG as data URL
```
The LLM extracts facts and asks questions; deterministic code validates and builds barcode data. If the LLM says "ready" but validation fails, the findings go back to the LLM once; otherwise the validator's message is shown.

## Status
Working: multi-turn chat, missing/conflict detection, label image in chat, mock and Gemini providers, 51 unit tests, printable label (Print button, true-size barcode). Not yet: scannability warning for undersized labels, endpoint tests, submission documentation.
