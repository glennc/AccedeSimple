# AccedeSimple Migration Guide

This document provides detailed guidance for the ongoing migrations to modernize AccedeSimple as a high-quality Microsoft AI sample.

---

## 🎯 Migration Overview

### Goals
1. **Remove Semantic Kernel Process dependencies** - Move to data pipeline patterns
2. **Modernize RAG implementation** - Use pure Microsoft.Extensions.VectorData
3. **Enhance Agent Framework usage** - Demonstrate advanced agent patterns
4. **Integrate DevUI** - Show modern developer experience
5. **Complete containerization** - All services deployable as containers
6. **Improve code quality** - Production-ready sample code

---

## 1. Semantic Kernel → Data Pipelines Migration

### Current Architecture

```
User Query → SearchService → SK VectorStoreCollection → SQLite → Results
                              ↑
                              SK Embeddings
```

**Files Using Semantic Kernel:**
- `src/AccedeSimple.Service/Program.cs` - Kernel setup, embedding generator
- `src/AccedeSimple.Service/Services/IngestionService.cs` - Text chunking via SK
- `src/AccedeSimple.Service/Services/SearchService.cs` - Vector search
- `src/AccedeSimple.Service/AccedeSimple.Service.csproj` - SK package references

### Target Architecture

```
User Query → Data Pipeline → VectorStore (Direct) → SQLite → Results
                ↓
            [Transform → Embed → Index]
```

### Migration Steps

#### Step 1: Research Data Pipelines
- [ ] Review Microsoft.Extensions.AI documentation for pipeline patterns
- [ ] Study examples of data transformation pipelines
- [ ] Understand how to compose embedding generation in pipelines
- [ ] Identify appropriate abstractions for RAG workflows

**Resources:**
- Microsoft.Extensions.AI GitHub repository
- .NET AI documentation on data pipelines
- Community samples demonstrating pipeline patterns

#### Step 2: Create Parallel Implementation
- [ ] Create `Services/PipelineIngestionService.cs` alongside existing service
- [ ] Implement pipeline-based text chunking
- [ ] Add embedding generation step in pipeline
- [ ] Test with existing policy documents

**Example Structure:**
```csharp
// Conceptual - adjust based on actual pipeline API
public class PipelineIngestionService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly VectorStoreCollection<int, Document> _collection;
    
    public async Task IngestAsync(string directory)
    {
        var pipeline = PipelineBuilder
            .FromDirectory(directory)
            .ChunkText(maxTokens: 500, overlap: 50)
            .GenerateEmbeddings(_embeddings)
            .StoreInVector(_collection)
            .Build();
            
        await pipeline.ExecuteAsync();
    }
}
```

#### Step 3: Update SearchService
- [ ] Keep `VectorStoreCollection` access
- [ ] Remove any SK-specific abstractions
- [ ] Use `Microsoft.Extensions.VectorData` directly

**Current Code:**
```csharp
// src/AccedeSimple.Service/Services/SearchService.cs
public async IAsyncEnumerable<Document> SearchAsync(string query)
{
    await foreach (var result in _collection.SearchAsync(query, top: 5))
    { 
        yield return result.Record;
    }
}
```

**Target:** Same interface, ensure no SK dependencies in implementation chain

#### Step 4: Update Program.cs
- [ ] Replace `kernel.Services.AddEmbeddingGenerator(...)` with direct registration
- [ ] Remove `Kernel` service registration if no longer needed
- [ ] Update `AddSqliteCollection` to use pure VectorData API

#### Step 5: Remove SK Packages
- [ ] Remove from `AccedeSimple.Service.csproj`:
  - `Microsoft.SemanticKernel.Process.Core`
  - `Microsoft.SemanticKernel.Process.LocalRuntime`
  - `Microsoft.SemanticKernel.Connectors.SqliteVec` (migrate to direct SQLite vector extension)
- [ ] Test all functionality
- [ ] Update documentation

### Testing Strategy
1. Create integration tests comparing old vs new RAG results
2. Verify embedding quality is maintained
3. Test ingestion performance
4. Validate search relevance scores

---

## 2. Agent Framework Enhancement

### Current State

**Existing Agents:**
- Policy Agent: Basic RAG-backed agent (`Program.cs`)
- Workflow Agents: Executors for travel planning, trip creation, approval

**Current Limitations:**
- Single-purpose agents without delegation
- No inter-agent communication patterns demonstrated
- Limited tool/function calling examples
- Minimal memory management

### Enhancement Plan

#### Phase 1: Enhance Policy Agent
**File:** `src/AccedeSimple.Service/Program.cs`

**Current:**
```csharp
builder.AddAIAgent("Policy", (sp, name) =>
{
    return sp.GetRequiredService<IChatClient>().CreateAIAgent("""
        Process the policy inquiry.
        Only use the search results to answer the user's question.
        Do not provide any additional information or context.
        Provide a summary of the policy based on the users' input and the search results from the policy documents.
        """, name, tools: [AIFunctionFactory.Create(sp.GetRequiredService<SearchService>().SearchAsync)]);
});
```

**Enhanced:**
```csharp
builder.AddAIAgent("Policy", (sp, name) =>
{
    var searchService = sp.GetRequiredService<SearchService>();
    var chatClient = sp.GetRequiredService<IChatClient>();
    
    return chatClient.CreateAIAgent(
        systemPrompt: """
        You are a corporate policy expert specializing in travel and expense policies.
        
        Your responsibilities:
        - Answer policy questions accurately using document search
        - Provide policy citations when making statements
        - Suggest related policies the user might need to know
        - Track conversation context to provide relevant follow-ups
        
        Use the search_policy tool to retrieve relevant policy documents.
        Always cite the specific policy section when answering.
        """,
        name: name,
        tools: 
        [
            AIFunctionFactory.Create(searchService.SearchAsync, 
                name: "search_policy",
                description: "Search corporate policy documents for relevant information")
        ],
        configuration: new AIAgentConfiguration
        {
            // Add memory, context window, etc.
        }
    );
});
```

#### Phase 2: Create Specialized Agents

**Create:** `src/AccedeSimple.Service/Agents/TravelAgent.cs`
```csharp
public class TravelAgent : AIAgent
{
    // Specialized agent for travel planning
    // - Can delegate to MCP server for flight searches
    // - Understands travel preferences and constraints
    // - Can consult policy agent for compliance checks
}
```

**Create:** `src/AccedeSimple.Service/Agents/ExpenseAgent.cs`
```csharp
public class ExpenseAgent : AIAgent
{
    // Specialized agent for expense processing
    // - Analyzes receipts
    // - Validates against policies
    // - Generates reports
}
```

#### Phase 3: Implement Agent Coordination

**Create:** `src/AccedeSimple.Service/Services/AgentCoordinator.cs`
```csharp
public class AgentCoordinator
{
    private readonly AIAgent _travelAgent;
    private readonly AIAgent _policyAgent;
    private readonly AIAgent _expenseAgent;
    
    public async Task<Response> HandleRequestAsync(UserRequest request)
    {
        // Route to appropriate agent
        // Coordinate multi-agent workflows
        // Handle agent-to-agent delegation
    }
}
```

### Documentation Requirements
- [ ] Document agent delegation patterns
- [ ] Show agent communication examples
- [ ] Explain when to create new agents vs. enhance existing
- [ ] Provide agent design guidelines

---

## 3. DevUI Integration

### Research Phase

**Questions to Answer:**
1. What is DevUI?
   - Is it part of Aspire 9.5+?
   - Is it a separate Microsoft tool?
   - Is it related to AI Toolkit?

2. What does it provide?
   - Development dashboard?
   - Agent debugging UI?
   - Workflow visualization?

3. How does it integrate?
   - Aspire hosting extension?
   - Standalone application?
   - VS Code extension?

### Investigation Tasks
- [ ] Check Aspire 9.5.1 release notes
- [ ] Search Microsoft announcements for "DevUI"
- [ ] Review AI Toolkit documentation
- [ ] Check Microsoft.Agents.AI preview documentation

### Placeholder Implementation Plan
Once identified, update this section with:
- Installation/setup steps
- Integration points in codebase
- Configuration requirements
- Usage documentation

---

## 4. Complete Containerization

### Current State

**Containerized:**
- ✅ `src/localguide/Dockerfile` - Python FastAPI service
- ✅ `src/webui/Dockerfile` - React frontend

**Missing:**
- ❌ Backend Service Dockerfile
- ❌ MCP Server Dockerfile

### Backend Service Dockerfile

**Create:** `src/AccedeSimple.Service/Dockerfile`

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY ["AccedeSimple.Service/AccedeSimple.Service.csproj", "AccedeSimple.Service/"]
COPY ["AccedeSimple.Domain/AccedeSimple.Domain.csproj", "AccedeSimple.Domain/"]
COPY ["AccedeSimple.ServiceDefaults/AccedeSimple.ServiceDefaults.csproj", "AccedeSimple.ServiceDefaults/"]
RUN dotnet restore "AccedeSimple.Service/AccedeSimple.Service.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/AccedeSimple.Service"
RUN dotnet build "AccedeSimple.Service.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "AccedeSimple.Service.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

COPY --from=publish /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "AccedeSimple.Service.dll"]
```

**Tasks:**
- [ ] Create Dockerfile
- [ ] Add health check endpoint to service
- [ ] Test local build: `docker build -t accedesimple-service .`
- [ ] Test local run with environment variables

### MCP Server Dockerfile

**Create:** `src/AccedeSimple.MCPServer/Dockerfile`

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["AccedeSimple.MCPServer/AccedeSimple.MCPServer.csproj", "AccedeSimple.MCPServer/"]
COPY ["AccedeSimple.Domain/AccedeSimple.Domain.csproj", "AccedeSimple.Domain/"]
COPY ["AccedeSimple.ServiceDefaults/AccedeSimple.ServiceDefaults.csproj", "AccedeSimple.ServiceDefaults/"]
RUN dotnet restore "AccedeSimple.MCPServer/AccedeSimple.MCPServer.csproj"

COPY . .
WORKDIR "/src/AccedeSimple.MCPServer"
RUN dotnet build "AccedeSimple.MCPServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AccedeSimple.MCPServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=publish /app/publish .

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "AccedeSimple.MCPServer.dll"]
```

### Docker Compose for Local Testing

**Create:** `docker-compose.yml` in root

```yaml
version: '3.8'

services:
  backend:
    build:
      context: ./src
      dockerfile: AccedeSimple.Service/Dockerfile
    ports:
      - "5000:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - MODEL_NAME=gpt-4.1
      - AZURE_OPENAI_ENDPOINT=${AZURE_OPENAI_ENDPOINT}
    depends_on:
      - mcpserver
      - localguide
    networks:
      - accedesimple

  mcpserver:
    build:
      context: ./src
      dockerfile: AccedeSimple.MCPServer/Dockerfile
    ports:
      - "5001:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - MODEL_NAME=gpt-4.1
      - AZURE_OPENAI_ENDPOINT=${AZURE_OPENAI_ENDPOINT}
    networks:
      - accedesimple

  localguide:
    build:
      context: ./src/localguide
      dockerfile: Dockerfile
    ports:
      - "8000:8000"
    environment:
      - PORT=8000
      - AZURE_OPENAI_ENDPOINT=${AZURE_OPENAI_ENDPOINT}
      - MODEL_NAME=gpt-4o-mini
    networks:
      - accedesimple

  webui:
    build:
      context: ./src/webui
      dockerfile: Dockerfile
    ports:
      - "3000:80"
    environment:
      - BACKEND_URL=http://backend:8080
    depends_on:
      - backend
    networks:
      - accedesimple

networks:
  accedesimple:
    driver: bridge
```

**Tasks:**
- [ ] Create docker-compose.yml
- [ ] Create .env.example with required variables
- [ ] Test: `docker-compose up`
- [ ] Document multi-container networking
- [ ] Add to README

---

## 5. Code Quality Improvements

### Remove Warning Suppressions

**Files with `#pragma warning disable`:**
- `src/AccedeSimple.Service/Program.cs`
- `src/AccedeSimple.Service/Endpoints.cs`
- `src/AccedeSimple.Service/Services/ProcessService.cs`
- `src/AccedeSimple.Service/Extensions.cs`
- Multiple executor files

**Process:**
1. Remove `#pragma warning disable` directive
2. Run `dotnet build` to see actual warnings
3. Fix warnings properly:
   - Add null checks
   - Fix async/await patterns
   - Remove unused variables
   - Fix naming conventions
4. For justified suppressions, use specific warning codes with comments

**Example:**
```csharp
// Before
#pragma warning disable
public async Task ProcessAsync(string? data)
{
    var result = await SomeMethod(data);
}
#pragma warning restore

// After
public async Task ProcessAsync(string? data)
{
    ArgumentNullException.ThrowIfNull(data);
    var result = await SomeMethod(data);
}
```

### Add XML Documentation

**Priority Files:**
1. All public classes in `Services/`
2. All executors in `Executors/`
3. Extension methods in `Extensions.cs`
4. Domain models in `AccedeSimple.Domain/`

**Example:**
```csharp
/// <summary>
/// Orchestrates the travel planning workflow, managing checkpoints and state transitions.
/// </summary>
/// <remarks>
/// This service coordinates between user input, AI agent execution, and admin approval processes.
/// It implements human-in-the-loop workflows using RequestPorts for pausing and resuming execution.
/// </remarks>
public class ProcessService
{
    /// <summary>
    /// Processes a user action based on classified intent, routing to appropriate handlers.
    /// </summary>
    /// <param name="userIntent">The classified user intent from the input message.</param>
    /// <param name="userInput">The original chat item from the user.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">Thrown when intent is unknown.</exception>
    public async Task ActAsync(UserIntent userIntent, ChatItem userInput)
    {
        // Implementation
    }
}
```

### Extract Constants

**Create:** `src/AccedeSimple.Service/Constants.cs`

```csharp
namespace AccedeSimple.Service;

/// <summary>
/// Application-wide constants for state management and configuration.
/// </summary>
public static class StateKeys
{
    /// <summary>
    /// Key format for workflow checkpoint metadata: "checkpoint-info:{tripId}"
    /// </summary>
    public const string CheckpointInfoFormat = "checkpoint-info:{0}";
    
    /// <summary>
    /// Key format for pending external requests: "pending-request:{tripId}"
    /// </summary>
    public const string PendingRequestFormat = "pending-request:{0}";
    
    /// <summary>
    /// Key for global list of trip approval requests
    /// </summary>
    public const string TripRequests = "trip-requests";
    
    /// <summary>
    /// Key for keyed chat history service
    /// </summary>
    public const string History = "history";
    
    /// <summary>
    /// Key format for trip options storage: "trip-options"
    /// </summary>
    public const string TripOptions = "trip-options";
}

/// <summary>
/// Request port identifiers used in workflow orchestration.
/// </summary>
public static class PortIds
{
    /// <summary>
    /// User selection port - waits for user to choose a trip itinerary
    /// </summary>
    public const string UserSelection = "UserSelection";
    
    /// <summary>
    /// Admin approval port - waits for admin approval or rejection
    /// </summary>
    public const string AdminApproval = "AdminApproval";
}
```

**Update usage:**
```csharp
// Before
_stateStore.Set($"checkpoint-info:{tripId}", checkpointInfo);

// After
_stateStore.Set(string.Format(StateKeys.CheckpointInfoFormat, tripId), checkpointInfo);
```

### Add Input Validation

**Domain Models - Add Annotations:**
```csharp
using System.ComponentModel.DataAnnotations;

public class TripRequest
{
    [Required(ErrorMessage = "Trip ID is required")]
    public required string TripId { get; set; }
    
    [Required]
    [StringLength(500, ErrorMessage = "Purpose cannot exceed 500 characters")]
    public required string Purpose { get; set; }
    
    [Required]
    [DataType(DataType.Currency)]
    [Range(0, double.MaxValue, ErrorMessage = "Total cost must be positive")]
    public decimal TotalCost { get; set; }
}
```

**Endpoints - Validate Requests:**
```csharp
group.MapPost("/requests/approval", async (
    [FromServices] ProcessService processService,
    [FromBody] TripRequestResult result,
    CancellationToken cancellationToken) =>
{
    // Validate
    if (string.IsNullOrWhiteSpace(result.TripId))
    {
        return Results.BadRequest(new { error = "TripId is required" });
    }
    
    if (!Enum.IsDefined(typeof(ApprovalState), result.Status))
    {
        return Results.BadRequest(new { error = "Invalid approval status" });
    }
    
    // Process
    await processService.ResumeWorkflowWithApprovalAsync(result.TripId, result);
    return Results.Ok();
});
```

---

## Testing Strategy

### Unit Tests
- [ ] Test data pipeline transformations
- [ ] Test agent delegation logic
- [ ] Test workflow state transitions
- [ ] Test validation logic

### Integration Tests
- [ ] Test multi-container deployment
- [ ] Test agent coordination
- [ ] Test checkpoint recovery
- [ ] Test RAG retrieval quality

### Manual Testing Checklist
- [ ] Deploy all services via docker-compose
- [ ] Test complete travel workflow
- [ ] Test admin approval flow
- [ ] Test policy Q&A
- [ ] Test local guide integration
- [ ] Verify telemetry in Aspire Dashboard

---

## Documentation Updates Required

### During Migration
- [ ] Update README.md with container deployment instructions
- [ ] Document new data pipeline approach
- [ ] Add agent coordination examples
- [ ] Update architecture diagrams

### After Migration
- [ ] Create CHANGELOG.md documenting breaking changes
- [ ] Update API documentation
- [ ] Add migration notes for consumers
- [ ] Create sample deployment guides (local, Azure)

---

## Timeline & Priorities

### Phase 1 (Immediate - Week 1-2)
1. Research data pipelines and DevUI
2. Create Dockerfiles for all services
3. Remove warning suppressions and add XML docs

### Phase 2 (Short-term - Week 3-4)
1. Implement data pipeline RAG migration
2. Enhance Policy agent
3. Extract constants and improve validation

### Phase 3 (Medium-term - Week 5-6)
1. Integrate DevUI (pending research)
2. Create specialized agents
3. Implement agent coordination

### Phase 4 (Polish - Week 7-8)
1. Complete testing
2. Update all documentation
3. Create deployment guides
4. Final code review and cleanup

---

## Success Metrics

- [ ] Zero Semantic Kernel Process dependencies in production code
- [ ] All services containerized and tested via docker-compose
- [ ] XML documentation coverage >90%
- [ ] No remaining `#pragma warning disable` without justification
- [ ] DevUI integrated and documented
- [ ] Multi-agent coordination demonstrated
- [ ] Comprehensive README with clear getting-started path
- [ ] All tests passing
- [ ] Aspire deployment working end-to-end

---

## Questions & Decisions Log

### Open Questions
1. **DevUI**: What is the exact technology/tool we're integrating?
2. **Data Pipelines**: Is there official Microsoft.Extensions.AI pipeline API, or should we create abstraction?
3. **Agent Memory**: What's the recommended pattern for conversation history in Microsoft.Agents.AI?

### Decisions Made
- Continue using SQLite for vector storage (lightweight, suitable for sample)
- Keep MCP server for tool calling demonstration (showcases protocol)
- Use docker-compose for local development story
- Target .NET 10 for all containerized services

---

*This document is living and should be updated as migrations progress and new information becomes available.*
