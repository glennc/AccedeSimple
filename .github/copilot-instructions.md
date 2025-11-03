# AccedeSimple - Travel Concierge System

## 🎯 What This Repository Does

**AccedeSimple** is an AI-powered travel management and expense approval system built with .NET 10, Aspire, and AI agents. It streamlines corporate travel planning by combining intelligent workflows, human-in-the-loop approvals, and multi-agent orchestration.

> **⚠️ Migration In Progress**: This sample is being modernized to showcase latest Microsoft AI patterns:
> - Migrating from Semantic Kernel to **Data Pipelines**
> - Expanding **Microsoft.Agents.AI** framework usage
> - Demonstrating **DevUI** developer experience
> - Adding comprehensive **containerization** for all services
> - Improving code quality for production-ready sample status

### Core Capabilities

1. **Intelligent Travel Planning** - Users request trips via natural language, and AI agents generate trip options (flights, hotels, car rentals)
2. **Human-in-the-Loop Workflows** - Implements checkpoint-based workflows that pause for user selection and admin approval
3. **Policy Question Answering** - RAG-based system answers company travel policy questions using vector search
4. **Local Guide Integration** - Python-based FastAPI service provides city attractions and recommendations
5. **Expense Management** - Tracks and manages travel-related expenses with approval workflows

---

## 🏗️ Architecture Overview

```
┌─────────────┐         ┌──────────────────┐         ┌──────────────┐
│   Web UI    │────────▶│  Backend Service │────────▶│  MCP Server  │
│  (React +   │  HTTP   │   (.NET Aspire)  │  HTTP   │   (Tools)    │
│   Vite)     │         │                  │         └──────────────┘
└─────────────┘         └──────────────────┘
                                │
                                ├─────────────────────┐
                                │                     │
                        ┌───────▼────────┐    ┌──────▼────────┐
                        │  LocalGuide    │    │ Azure OpenAI  │
                        │  (Python/      │    │  & Storage    │
                        │   FastAPI)     │    │               │
                        └────────────────┘    └───────────────┘
```

### Key Components

- **AppHost** (`src/AccedeSimple.AppHost/`) - Aspire orchestration host, wires up all services
- **Backend Service** (`src/AccedeSimple.Service/`) - Main API handling chat, workflows, and business logic
- **Domain** (`src/AccedeSimple.Domain/`) - Shared types: `UserIntent`, `TripRequest`, `ApprovalState`, etc.
- **MCP Server** (`src/AccedeSimple.MCPServer/`) - Model Context Protocol server exposing travel booking tools
- **LocalGuide** (`src/localguide/`) - Python FastAPI service for city attraction recommendations
- **Web UI** (`src/webui/`) - React frontend with chat interface and admin dashboard

---

## 🧠 How It Works

### 1. User Intent Classification

When a user sends a message, the system classifies intent:

- **`General`** - Generic chat handled by base LLM
- **`AskLocalGuide`** - Routes to Python LocalGuide service for attractions
- **`AskPolicyQuestions`** - Uses RAG with policy documents from `docs/` folder
- **`StartTravelPlanning`** - Kicks off the travel workflow
- **`StartTripApproval`** - Resumes workflow after user selects itinerary

**Location:** `src/AccedeSimple.Domain/UserIntent.cs`

### 2. Travel Workflow (Human-in-the-Loop)

The travel planning workflow uses **checkpointing** to pause and resume:

```
TravelPlanningExecutor
    ↓ (generates trip options via MCP)
UserSelectionPort ⏸️ PAUSES - waits for user to pick option
    ↓ (user selects)
TripRequestCreationExecutor
    ↓ (creates approval request)
AdminApprovalPort ⏸️ PAUSES - waits for admin approval
    ↓ (admin approves/rejects)
ApprovalResponseExecutor
    ↓ (sends final response)
```

**Key Files:**
- Workflow definition: `src/AccedeSimple.Service/Extensions.cs` (`AddTravelWorkflow`)
- Workflow orchestration: `src/AccedeSimple.Service/Services/ProcessService.cs`
- Executors: `src/AccedeSimple.Service/Executors/`

**Checkpoint Storage:** Uses `StateStore` (in-memory) to persist workflow state between pause/resume

### 3. Request Ports (Pause Points)

The workflow uses **RequestPorts** to pause execution:

- **`UserSelectionPort`** - Input: `List<TripOption>`, waits for: `ItinerarySelectedChatItem`
- **`AdminApprovalPort`** - Input: `TripRequest`, waits for: `TripRequestResult`

When a port is reached:
1. Workflow emits `RequestInfoEvent`
2. System stores checkpoint and `ExternalRequest` in `StateStore`
3. Message sent to user/admin
4. Workflow waits until `ResumeWorkflowAsync` is called with response

### 4. MCP Server (Tool Execution)

The MCP Server exposes tools that AI agents can call:

- **`SearchTripOptions`** - Uses Azure OpenAI to generate realistic trip options based on `TripParameters`

**Location:** `src/AccedeSimple.MCPServer/Program.cs`

The backend connects via SSE (Server-Sent Events) to the MCP server for tool invocation.

### 5. Policy RAG System

Policy documents are:
1. Ingested from `src/AccedeSimple.Service/docs/` on startup
2. Embedded using `text-embedding-3-small`
3. Stored in SQLite vector database (`SqliteVec`)
4. Queried via `SearchService` when user asks policy questions

**Key Files:**
- Ingestion: `src/AccedeSimple.Service/Services/IngestionService.cs`
- Search: `src/AccedeSimple.Service/Services/SearchService.cs`
- Agent: Configured in `Program.cs` as `AIAgent("Policy")`

### 6. LocalGuide (Python Service)

FastAPI service that:
1. Receives queries like "attractions in Seattle"
2. Uses `pydantic-ai` with Azure OpenAI to generate structured `CityAttractions` objects
3. Returns formatted attraction lists

**Location:** `src/localguide/main.py`

---

## 📁 Project Structure

```
AccedeSimple/
├── src/
│   ├── AccedeSimple.AppHost/          # Aspire orchestration
│   │   └── AppHost.cs                 # Service wiring & configuration
│   │
│   ├── AccedeSimple.Service/          # Main backend API
│   │   ├── Program.cs                 # Startup & dependency injection
│   │   ├── Endpoints.cs               # HTTP endpoints (chat, admin)
│   │   ├── ChatStream.cs              # SSE message streaming
│   │   ├── StateStore.cs              # In-memory state management
│   │   ├── Services/
│   │   │   ├── ProcessService.cs      # Workflow orchestration logic
│   │   │   ├── MessageService.cs      # Chat history management
│   │   │   ├── SearchService.cs       # Vector search for policies
│   │   │   └── IngestionService.cs    # Policy document ingestion
│   │   └── Executors/
│   │       ├── TravelPlanningExecutor.cs
│   │       ├── TripRequestCreationExecutor.cs
│   │       └── ApprovalResponseExecutor.cs
│   │
│   ├── AccedeSimple.Domain/           # Shared domain models
│   │   ├── UserIntent.cs
│   │   ├── Approvals/                 # Approval-related types
│   │   ├── Bookings/                  # Trip/booking types
│   │   ├── Expenses/                  # Expense tracking
│   │   └── Shared/                    # Common types
│   │
│   ├── AccedeSimple.MCPServer/        # MCP tool provider
│   │   └── Program.cs                 # SearchTripOptions tool
│   │
│   ├── localguide/                    # Python FastAPI service
│   │   ├── main.py                    # City attractions endpoint
│   │   └── pyproject.toml
│   │
│   └── webui/                         # React frontend
│       ├── src/
│       │   ├── components/            # ChatContainer, AdminPage
│       │   ├── services/              # API clients
│       │   └── types/                 # TypeScript definitions
│       └── vite.config.ts
│
├── AccedeSimple.EvalTests/            # Evaluation tests (WIP)
└── docs/                              # Project documentation
```

---

## 🔑 Key Concepts for Development

### State Management

- **`StateStore`** - In-memory dictionary for workflow checkpoints, trip requests, and chat history
- **Checkpoint Keys:**
  - `checkpoint-info:{tripId}` - Workflow checkpoint metadata
  - `pending-request:{tripId}` - `ExternalRequest` waiting for response
  - `trip-requests` - Global list of pending admin approvals
- **History Key:** `"history"` - Keyed service storing `ConcurrentDictionary<userId, List<ChatItem>>`

### Chat Message Types

All messages inherit from `ChatItem` and flow through `ChatStream`:

- **`UserMessage`** - User input
- **`AssistantResponse`** - AI-generated text responses
- **`CandidateItineraryChatItem`** - Trip options for user selection
- **`ItinerarySelectedChatItem`** - User's trip selection
- **`TripRequestUpdated`** - Admin page notifications
- **`TripRequestDecisionChatItem`** - Final approval/rejection message

### Workflow Resumption Pattern

```csharp
// Starting new workflow
await RunOrResumeWorkflowAsync(
    workflow: workflow,
    workflowName: "travel workflow",
    tripId: Guid.NewGuid().ToString(),  // New ID
    data: userMessage                   // Input data
);

// Resuming from checkpoint
await RunOrResumeWorkflowAsync(
    workflow: workflow,
    workflowName: "travel workflow",
    tripId: existingTripId,             // Existing ID
    data: userSelectionOrApprovalResult // Response data
);
```

The method automatically detects checkpoint existence and chooses start vs. resume logic.

### Azure OpenAI Configuration

All AI services use Azure OpenAI with **Entra ID authentication**:
- Model: `gpt-4.1` (configurable via `MODEL_NAME` env var)
- Embeddings: `text-embedding-3-small`
- Auth: `DefaultAzureCredential` (no API keys)

**User Secrets Required:**
```bash
cd src/AccedeSimple.AppHost
dotnet user-secrets set "AzureOpenAI:ResourceGroup" "your-rg"
dotnet user-secrets set "AzureOpenAI:ResourceName" "your-openai"
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://your-openai.openai.azure.com/"
dotnet user-secrets set "Azure:SubscriptionId" "your-sub-id"
dotnet user-secrets set "AzureAIFoundry:Project" "your-project"
```

---

## 🚀 Development Workflows

### Running Locally

1. **Start everything:**
   ```bash
   cd src/AccedeSimple.AppHost
   dotnet run
   ```
   This launches:
   - Backend service (port varies)
   - MCP Server
   - LocalGuide Python service (port 8000)
   - Web UI (port 35369)
   - Aspire Dashboard

2. **Access:**
   - **Web UI:** http://localhost:35369
   - **Aspire Dashboard:** Shown in terminal output
   - **LocalGuide:** http://localhost:8000

### Adding New Executors

1. Create executor in `src/AccedeSimple.Service/Executors/`
2. Implement `IAsyncExecutor<TInput, TOutput>`
3. Register in `Extensions.cs` → `AddTravelWorkflow()`
4. Add to workflow builder chain

### Adding New Chat Message Types

1. Define type in `src/AccedeSimple.Domain/`
2. Inherit from `ChatItem`
3. Set `IsUserVisible = true/false`
4. Handle in `Endpoints.cs` → `HandleMessageAsync()`

### Testing Workflows

Current approach (as evals are WIP):
1. Use Web UI to trigger workflow
2. Monitor Aspire Dashboard for logs
3. Check workflow events in console output

**Future:** Use `AccedeSimple.EvalTests/` for automated evaluation

---

## 🔧 Common Tasks

### Change the AI Model

Update `MODEL_NAME` environment variable in `AppHost.cs`:
```csharp
var modelName = "gpt-4o"; // Change here
```

### Add New User Intent

1. Add enum value to `src/AccedeSimple.Domain/UserIntent.cs`
2. Add case in `ProcessService.ActAsync()`
3. Implement handler logic

### Modify Trip Options Format

Edit MCP Server's `SearchTripOptions` tool:
- File: `src/AccedeSimple.MCPServer/Program.cs`
- Update `TripOption` model in Domain project
- Adjust prompt in `GetPrompt()` method

### Store Workflow State Persistently

Currently uses in-memory `StateStore`. To persist:
1. Replace `StateStore` with database-backed implementation
2. Serialize checkpoints to storage (SQL, Cosmos DB, etc.)
3. Update `ProcessService` to load checkpoints on startup

---

## 🐛 Known Issues & Migration Status

### 🚧 Active Migrations
- [ ] **Semantic Kernel → Data Pipelines** - Replacing SK Process with data pipeline patterns
- [ ] **RAG Modernization** - Moving to pure Microsoft.Extensions.VectorData without SK wrappers
- [ ] **DevUI Integration** - Adding new developer experience tooling (research phase)
- [ ] **Complete Containerization** - Adding Dockerfiles for backend and MCP server

### Cleanup Tasks
- [ ] **Code Quality** - Remove `#pragma warning disable`, add XML docs, proper error handling
- [ ] **State Management** - Need persistent storage (Azure Tables/Cosmos) for production
- [ ] **Python OTEL** - OpenTelemetry not configured for LocalGuide
- [ ] **Evaluations** - `AccedeSimple.EvalTests` incomplete

### Completed
- [x] **State Issue** - Fixed checkpoint management
- [x] **Agent Framework** - Migrated to Microsoft.Agents.AI.Workflows
- [x] **Aspire Integration** - Full .NET Aspire orchestration
- [x] **Python Service** - LocalGuide FastAPI integration
- [x] **UI** - React frontend with SSE streaming

---

## 📚 Key Dependencies

| Technology | Purpose | Status |
|-----------|---------|--------|
| **.NET 10 + Aspire** | Service orchestration, configuration, telemetry | ✅ Active |
| **Microsoft.Extensions.AI** | Unified AI client abstraction | ✅ Active |
| **Microsoft.SemanticKernel** | ~~Plugin system, RAG, embeddings~~ | 🔄 Being Replaced |
| **Microsoft.Agents.AI** | Workflow engine, checkpointing, RequestPorts | ✅ Active |
| **Data Pipelines** | Modern data processing and RAG patterns | 🚧 In Progress |
| **ModelContextProtocol** | MCP client/server for tool calling | ✅ Active |
| **SqliteVec** | Vector database for policy documents | ✅ Active |
| **Microsoft.Extensions.VectorData** | Vector storage abstraction | ✅ Active |
| **Azure OpenAI** | LLM & embeddings | ✅ Active |
| **FastAPI + pydantic-ai** | Python local guide service | ✅ Active |
| **React + Vite** | Frontend UI | ✅ Active |

---

## 🎓 Learning Resources

- **Aspire:** https://learn.microsoft.com/dotnet/aspire
- **Microsoft.Agents.AI Workflows:** Internal preview docs (check team wiki)
- **MCP Protocol:** https://modelcontextprotocol.io
- **Microsoft.Extensions.AI:** https://devblogs.microsoft.com/dotnet/introducing-microsoft-extensions-ai-preview/

---

## 💡 Copilot Tips

When working with this codebase:

### General Development
1. **For workflow changes:** Always check `ProcessService.cs` first - it's the central orchestrator
2. **For new endpoints:** Add to `Endpoints.cs` and follow existing patterns (`MapChatEndpoints`, `MapAdminEndpoints`)
3. **For AI features:** Use `IChatClient` abstraction - don't couple to specific providers
4. **For state:** Be mindful that `StateStore` is in-memory - data is lost on restart
5. **For debugging:** Use Aspire Dashboard to trace requests across services
6. **For type safety:** Domain models are shared - changes ripple across projects

### Migration-Specific Guidance
7. **Semantic Kernel → Data Pipelines:**
   - Start with `SearchService.cs` and `IngestionService.cs` (smallest scope)
   - Research Microsoft.Extensions.AI pipeline patterns before implementing
   - Keep vector storage on SQLite, modernize access patterns
   - Create parallel implementations to test before removing SK code

8. **Agent Framework Enhancements:**
   - Current workflow is in `Extensions.cs::AddTravelWorkflow`
   - Policy agent in `Program.cs` is minimal - enhance with memory & tools
   - Consider adding specialized agents for travel, policy, and expense domains
   - Document agent delegation and communication patterns

9. **Code Quality:**
   - Remove `#pragma warning disable` statements and fix underlying issues
   - Add XML documentation comments to all public APIs
   - Extract magic strings to constants (especially StateStore keys)
   - Add proper error handling with structured responses

10. **Containerization:**
    - When creating Dockerfiles, base on .NET 10 runtime images
    - Ensure multi-stage builds for smaller images
    - Include health check endpoints
    - Document environment variables required per container

---

## 📝 Deployment

Uses Azure Developer CLI (`azd`):

```bash
azd init  # First time only
azd up    # Deploy everything
```

Deploys to **Azure Container Apps** with:
- Azure OpenAI (existing resource)
- Azure Storage (for uploads)
- Container Apps (backend, MCP, localguide, webui)

See `azure.yaml` for deployment configuration.

---

## ⚠️ Important Notes

1. **Demo purposes only** - Not production-ready (lacks auth, validation, error handling)
2. **No authentication** - Assumes trusted environment
3. **In-memory state** - Workflows don't survive restarts
4. **Azure OpenAI required** - Cannot run offline without model changes
5. **Storage emulator** - For local dev, uses Azurite (converts to data URIs for LLM)

---

## 🤝 Contributing

When adding features:
1. Follow existing patterns (Executors for workflow steps, Services for business logic)
2. Use `ILogger` for diagnostics
3. Add OpenTelemetry spans for observability
4. Update this document if architecture changes

---

**Last Updated:** October 31, 2025  
**Version:** 1.0  
**Maintainer:** AccedeSimple Team
