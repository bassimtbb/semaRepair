# SemaRepair Chatbot

AI-powered car repair assistant for Italian mechanics.
Built with ASP.NET Core 8, React, PostgreSQL + pgvector, and Google Gemini.

## Features

- Natural language car identification in Italian
- Fault code (codice guasto) search — finds vehicles with documented cases
- Symptom-based repair document search using vector similarity
- Voice input via Gemini AI transcription
- Repair documents matching the official SemaRepair template
- Streaming responses via Server-Sent Events

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | React 18 + TypeScript + Vite |
| Backend | ASP.NET Core 8 Web API |
| Database | PostgreSQL 16 + pgvector |
| AI | Google Gemini 2.5 Flash + gemini-embedding-001 |
| Infrastructure | Docker + Docker Compose |

## Project Structure

```
SemaRepair/
├── backend/          ASP.NET Core 8 API
├── frontend/         React + TypeScript SPA
├── dataseeder/       Python data loader
├── data/             PostgreSQL data (not committed)
└── docker-compose.yml
```

## Getting Started

1. Clone the repository
2. Copy `.env.example` to `.env` and fill in your values
3. Add your Excel data file to `dataseeder/GUP_PER_IA.xlsx`
4. Run:

```bash
docker compose up -d --build
```

5. Wait for the dataseeder to load the data (~15 seconds)
6. Wait for the backend to generate embeddings (~5 minutes on first run)
7. Open http://localhost:3000

## Environment Variables

See `.env.example` for required variables.
Never commit `.env` — it contains your API keys.

## Data

The Excel file `GUP_PER_IA.xlsx` is not included in this repository.
Place it in the `dataseeder/` folder before running.
