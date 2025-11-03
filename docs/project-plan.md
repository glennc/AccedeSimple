# Project Plan

## ✅ Completed Tasks

- [X] Create agent to trigger workflows
- [X] Refactor into web service
- [X] Add Aspire
- [X] Configure OTEL
- [X] Add MCP Server
- [X] Simplify domain model
- [X] Add UI
    - [X] Connect UI to Backend
    - [X] Handle file uploads
    - [X] Reconcile message types
- [X] Configure deployment
- [X] Add Python
- [X] Set up Aspire Hosting for AI Models
- [X] Fix data generation
- [X] Fix deployment
- [X] Fix Python .NET requests
- [X] Figure out state issue [#1](https://github.com/luisquintanilla/AccedeSimple/issues/1)

## 🚧 In Progress - Core Migrations

### 1. Migrate from Semantic Kernel to Data Pipelines
**Priority:** High  
**Status:** Not Started

**Current State:**
- Using `Microsoft.SemanticKernel.Process.Core` and `Microsoft.SemanticKernel.Process.LocalRuntime` (v1.66.0-alpha)
- RAG system uses `Microsoft.SemanticKernel.Connectors.SqliteVec` for vector storage
- Embedding generation via Semantic Kernel

**Migration Tasks:**
- [ ] **Research Data Pipelines API** - Understand the new Microsoft.Extensions.AI data pipeline patterns
  - Location: Check latest Microsoft.Extensions.AI docs and samples
  - Goal: Understand pipeline composition, data transformation patterns
  
- [ ] **Replace RAG Implementation** 
  - Current: `src/AccedeSimple.Service/Services/IngestionService.cs` - Uses SK's text chunking
  - Current: `src/AccedeSimple.Service/Services/SearchService.cs` - Uses VectorStoreCollection
  - Target: Migrate to data pipeline-based ingestion and retrieval
  - Start here: Create new pipeline-based ingestion service
  
- [ ] **Update Vector Storage Layer**
  - Current: `Microsoft.SemanticKernel.Connectors.SqliteVec`
  - Target: Use `Microsoft.Extensions.VectorData.Abstractions` directly with data pipelines
  - Keep SQLite backend, modernize access pattern
  
- [ ] **Remove SK Dependencies**
  - Files to update: `src/AccedeSimple.Service/AccedeSimple.Service.csproj`
  - Remove: `Microsoft.SemanticKernel.Process.*` packages
  - Keep: `Microsoft.Extensions.VectorData.Abstractions` (already present)

### 2. Adopt Agent Framework
**Priority:** High  
**Status:** Partially Complete (Already using Microsoft.Agents.AI)

**Current State:**
- ✅ Already using `Microsoft.Agents.AI.Workflows` (v1.0.0-preview.251028.1)
- Workflow-based execution with RequestPorts and checkpointing
- Executors: `TravelPlanningExecutor`, `TripRequestCreationExecutor`, `ApprovalResponseExecutor`

**Enhancement Tasks:**
- [ ] **Expand Agent Framework Usage**
  - Current: Basic workflow orchestration in `Extensions.cs`
  - Target: Demonstrate advanced agent patterns (delegation, tool calling, multi-agent coordination)
  - Start here: `src/AccedeSimple.Service/Extensions.cs` - AddTravelWorkflow method
  
- [ ] **Add Agent-to-Agent Communication**
  - Create specialized agents for different domains (travel, policy, expense)
  - Implement agent delegation patterns
  - Document inter-agent messaging
  
- [ ] **Enhance Policy Agent**
  - Current: Basic `AIAgent("Policy")` in `Program.cs`
  - Target: Full-featured agent with memory, tool access, and context management
  - Start here: `src/AccedeSimple.Service/Program.cs` - AIAgent configuration

### 3. Integrate DevUI
**Priority:** Medium  
**Status:** Not Started

**Research Needed:**
- [ ] **Identify DevUI Technology**
  - Is this a new Aspire dashboard feature?
  - Is this a separate UI framework for development?
  - Check latest Aspire 9.x documentation for DevUI references
  
- [ ] **Implementation Plan** (Pending research)
  - Location: TBD based on what DevUI is
  - Integration point: Likely `src/AccedeSimple.AppHost/`
  - Goal: Demonstrate developer experience improvements

**Potential Integration Points:**
- If DevUI is Aspire-related: Update `AppHost.cs` configuration
- If DevUI is standalone: Add new project or extend webui
- Document developer experience workflows

### 4. Container Deployment Demonstration
**Priority:** Medium  
**Status:** Partially Complete

**Current State:**
- ✅ Dockerfiles exist for `localguide` and `webui`
- ✅ Azure deployment configured via `azure.yaml` → Container Apps
- ✅ Dev Container configured (`.devcontainer.json`)
- Aspire uses `PublishAsDockerFile()` for webui

**Enhancement Tasks:**
- [ ] **Add Dockerfile for Backend Service**
  - Location: Create `src/AccedeSimple.Service/Dockerfile`
  - Base on .NET 10 runtime
  - Include policy docs in container
  
- [ ] **Add Dockerfile for MCP Server**
  - Location: Create `src/AccedeSimple.MCPServer/Dockerfile`
  - Ensure SSE endpoint accessibility
  
- [ ] **Document Local Container Deployment**
  - Create docker-compose.yml for local multi-container testing
  - Document container-to-container networking
  - Show how to run without Aspire for pure container scenario
  
- [ ] **Enhance Container Configuration**
  - Add health checks to all Dockerfiles
  - Implement graceful shutdown
  - Document environment variable requirements per container

### 5. Code Cleanup for Sample Quality
**Priority:** High  
**Status:** Not Started

**Areas Requiring Cleanup:**

- [ ] **Remove #pragma warning disable**
  - Files: Multiple (search for `#pragma warning disable`)
  - Action: Fix warnings properly or add specific suppressions with justification
  
- [ ] **Add XML Documentation**
  - Priority files:
    - `src/AccedeSimple.Service/Services/ProcessService.cs`
    - `src/AccedeSimple.Service/Executors/*.cs`
    - `src/AccedeSimple.Service/Extensions.cs`
  - Add: Summary, param, returns tags
  
- [ ] **Implement Proper Error Handling**
  - Current: Many catch blocks with just logging
  - Target: Structured error responses, retry logic, circuit breakers
  - Start here: `src/AccedeSimple.Service/Endpoints.cs`
  
- [ ] **Extract Magic Strings to Constants**
  - Current: Hardcoded strings like "trip-requests", "history", "checkpoint-info:{tripId}"
  - Target: Create `Constants.cs` with well-documented keys
  - Location: `src/AccedeSimple.Service/StateStore.cs` area
  
- [ ] **Improve Code Comments**
  - Add "Why" comments for non-obvious decisions
  - Document workflow state transitions
  - Explain RequestPort pause/resume mechanics
  
- [ ] **Standardize Naming Conventions**
  - Review: Inconsistent casing in some variable names
  - Ensure: Consistent async method naming (Async suffix)
  
- [ ] **Add Input Validation**
  - Endpoints: Validate all incoming request models
  - Add: Data annotations to domain models
  - Create: Validation middleware or filters

## 📋 Additional Tasks

### Testing & Quality
- [ ] **Add Evals** (Original task)
  - Location: `AccedeSimple.EvalTests/`
  - Implement: AI quality evaluations using Microsoft.Extensions.AI.Evaluation
  
- [ ] **Add Python OTEL** (Original task)
  - File: `src/localguide/main.py`
  - Add: OpenTelemetry instrumentation for Python service
  - Ensure: Traces appear in Aspire Dashboard

### State Management
- [ ] **Set up persistent storage for state** (Original task - now enhanced)
  - Current: In-memory `StateStore`
  - Options: Azure Table Storage, Cosmos DB, or distributed cache
  - Priority: Medium (needed for production scenarios)

---

## 📍 Getting Started Guide

### For Migration from Semantic Kernel to Data Pipelines:
1. Start with `SearchService.cs` - this is the simplest RAG component
2. Research Microsoft.Extensions.AI pipeline patterns
3. Create parallel implementation to compare
4. Then tackle `IngestionService.cs`

### For Agent Framework Enhancements:
1. Review current workflow in `Extensions.cs::AddTravelWorkflow`
2. Study Microsoft.Agents.AI documentation for advanced patterns
3. Start by enhancing the Policy agent in `Program.cs`
4. Add new specialized agents incrementally

### For DevUI Integration:
1. **Research first** - Determine what DevUI is
2. Check Aspire 9.5+ release notes
3. Look for Microsoft announcements about developer UI tooling

### For Container Deployment:
1. Start with backend Dockerfile (most complex)
2. Test locally with `docker build` and `docker run`
3. Create docker-compose.yml for integration testing
4. Document the multi-container startup sequence

### For Code Cleanup:
1. Begin with removing `#pragma warning disable`
2. Run `dotnet build` with warnings as errors
3. Fix issues systematically by file
4. Add XML docs as you go

---

## 🎯 Success Criteria

- [ ] Zero Semantic Kernel dependencies in production code
- [ ] Data pipeline-based RAG system operational
- [ ] Multiple agents demonstrating delegation and coordination
- [ ] DevUI integrated and documented
- [ ] All services containerized with Dockerfiles
- [ ] Sample code is clean, well-documented, and production-quality
- [ ] Comprehensive README with architecture decision records