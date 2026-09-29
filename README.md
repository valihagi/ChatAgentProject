# Label Chat Agent

Chat agent that turns natural-language product/packaging info into print-ready beverage labels using the TEC-IT Barcode API. Home task for TEC-IT (.NET full-stack).

## Stack
- Backend: ASP.NET Core (.NET 10) minimal API, `src/ChatAgent.Api`
- Frontend: plain HTML/CSS/JS in `src/ChatAgent.Api/wwwroot` (served by the API, no build step)
- LLM: Google Gemini (free tier), behind the `IChatModel` interface; a mock is the default

## Run
```bash
dotnet run --project src/ChatAgent.Api      # http://localhost:5080
```

## Configuration
Secrets come from environment variables only and are never committed.

| Variable / setting | Purpose |
|---|---|
| `Chat__Provider` | `Mock` (default) or `Gemini` |
| `GEMINI_API_KEY` | Gemini API key (only with `Gemini`) |
| `Gemini__Model` | Optional model override |
| `TECIT_ACCESS_ID` | TEC-IT Barcode API access id |

## Status
Base setup: chat UI + `POST /api/chat`. Agent logic and barcode integration are next.
