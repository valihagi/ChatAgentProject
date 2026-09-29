# Label Chat Agent

Chat agent that turns natural-language product/packaging info into print-ready beverage labels using the TEC-IT Barcode API. Home task for TEC-IT (.NET full-stack).

## Stack
- Backend: ASP.NET Core (.NET 10) minimal API, `src/ChatAgent.Api`
- Frontend: plain HTML/CSS/JS in `src/ChatAgent.Api/wwwroot` (served by the API, no build step)
- LLM: Google Gemini `gemini-3.8-flash` (free tier), behind the `IChatModel` interface; a mock is the default

## Run
```bash
dotnet run --project src/ChatAgent.Api      # http://localhost:5080
```

## Configuration
Secrets come from environment variables only and are never committed.

| Variable / setting | Purpose |
|---|---|
| `Chat__Provider` | `Mock` (default) or `Gemini` |
| `GEMINI_API_KEY` | Gemini API key (only with `Gemini`); store with `dotnet user-secrets` |
| `Gemini__Model` | Optional model override |
| `TECIT_ACCESS_ID` | TEC-IT Barcode API access id |

## Barcode API notes (observed)
- Requests are sent as POST so the access id never appears in a URL.
- Errors are returned as HTTP 200 with an `image/gif` error bitmap (`onerror=500` is not honoured), so the client treats any media type different from the requested one as a failure.
- This access id behaves like a non-subscriber: max 300 DPI, no SVG, per-IP rate limit.
- The API does not validate GS1 check digits (a wrong GTIN check digit in GS1-128 still renders), so the backend must validate them.

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
Working: multi-turn chat, missing/conflict detection, label image in chat, mock and Gemini providers, 38 unit tests. Not yet: prompt tuning against real Gemini conversations, printable label layout, submission documentation.
