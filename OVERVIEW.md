# LocalAI Knowledge Codebase Analysis

## Solution Structure

The solution follows a **Clean Architecture** pattern with five main projects:

1. **LocalAI.Core** - Domain layer (interfaces and models)
2. **LocalAI.Infrastructure** - Data layer (implementations)
3. **LocalAI.Console** - Console application UI
4. **LocalAI.Api** - RESTful API service
5. **LocalAI.Web** - Blazor web UI

### Project Dependencies

```
LocalAI.Console ──┐
                  ├──► LocalAI.Core
LocalAI.Web ──────┤
                  └──► LocalAI.Infrastructure
LocalAI.Api ──────┘
```

## Core Architecture Components

### 1. LocalAI.Core (Domain Layer)
Contains the fundamental business logic and contracts:

**Interfaces:**
- `IEmbeddingService` - Generates vector embeddings from text
- `IVectorSearchService` - Manages vector database operations
- `IDocumentProcessor` - Processes documents into searchable chunks
- `IRAGService` - Generates AI responses using retrieved context
- `IConversationService` - Manages chat conversations
- `IDisplayService` - Formats output for display

**Models:**
- `SearchResult` - Represents a document chunk with relevance score
- `DocumentChunk` - A chunk of processed text with metadata
- `ChatSession` - Conversation session tracking
- `ConversationExchange` - User/assistant message pairs

### 2. LocalAI.Infrastructure (Data Layer)
Provides concrete implementations of core interfaces:

**Key Services:**
- `EmbeddingService` - Uses LM Studio/OpenRouter for embeddings
- `VectorSearchService` - Integrates with Qdrant vector database
- `DocumentProcessor` - Handles PDF, text, and web content processing
- `RAGService` - Combines search results with LLM for contextual responses
- `Qwen3CoderService` - Specialized code assistant using OpenRouter

**Data Management:**
- SQLite database for chat session persistence
- Entity Framework Core for ORM
- File-based metadata tracking for processed documents

### 3. LocalAI.Console (Presentation Layer)
Interactive command-line interface for:

1. **Document Processing Pipeline:**
   - Scans configured directories for documents
   - Processes PDFs and text files into chunks
   - Generates embeddings via LM Studio
   - Stores vectors in Qdrant database

2. **Interactive Search:**
   - Accepts user queries
   - Searches Qdrant for relevant chunks
   - Generates RAG-enhanced responses
   - Displays results with source attribution

### 4. LocalAI.Api (API Layer)
RESTful service exposing functionality to other applications:

**Key Endpoints:**
- `/api/search` - Semantic search with RAG response generation
- `/api/documents/process` - Process all documents in configured directories
- `/api/documents/upload` - Upload and process individual files
- `/api/conversations` - Chat session management
- `/api/code` - Specialized code assistant (when OpenRouter enabled)

**Features:**
- Swagger documentation at `/swagger`
- CORS support for web UI
- Debug endpoints for troubleshooting
- Health checks and status monitoring

### 5. LocalAI.Web (Presentation Layer)
Blazor Server web application with Claude-style UI:

**Key Components:**
- **SearchPage** - Main chat interface with conversation history
- **Documents** - Document processing and management
- **Dashboard** - System metrics and status
- **Settings** - Configuration management

**Features:**
- Real-time chat with typing indicators
- Conversation history with local storage
- Source attribution with expandable cards
- Mobile-responsive design
- Dark/light theme support

## Data Flow & Processing Pipeline

### 1. Document Processing Flow
```
Documents → DocumentProcessor → Text Chunks → EmbeddingService → VectorSearchService → Qdrant
     ↓              ↓                ↓              ↓                    ↓           ↓
  PDF/TXT    Extract & Chunk    Generate     Store Vectors      Store in       Vector DB
             Text with Metadata Embeddings   with Metadata      Collection
```

### 2. Search & RAG Flow
```
User Query → VectorSearchService → Search Results → RAGService → LLM Response → Display
     ↓              ↓                   ↓              ↓              ↓           ↓
  Embedding    Query Vector DB    Scored Chunks   Context + Query  AI Response  Formatted
  Generation   with Similarity    with Sources    to LLM          Synthesis    Output
```

## Key Technologies & Libraries

### Core Framework
- **.NET 9.0** - Primary development platform
- **Entity Framework Core** - Database ORM
- **Dependency Injection** - Service management

### External Services
- **Qdrant** - Vector database for semantic search
- **LM Studio** - Local LLM hosting (primary)
- **OpenRouter** - Cloud LLM access (optional)
- **PdfPig** - PDF text extraction

### Web Technologies
- **Blazor Server** - Web UI framework
- **Bootstrap 5** - Responsive styling
- **Markdig** - Markdown processing
- **HTML Agility Pack** - Web content parsing

## Configuration & Environment

### Configuration Sources
1. `appsettings.json` - Base configuration
2. `.env` - Environment variables (API keys)
3. Environment variables - Runtime overrides

### Key Settings
- **EmbeddingService** - LM Studio endpoint and model
- **RAGService** - LLM endpoint and model
- **Qdrant** - Vector database connection
- **DocumentPaths** - Source directories for processing
- **OpenRouter** - Optional cloud LLM configuration

## Docker & Deployment

### Container Architecture
1. **API Service** - Main application backend
2. **Web UI Service** - Blazor frontend
3. **Qdrant Service** - Vector database
4. **LM Studio** - Local LLM (external)

### Configuration
- Multi-stage Dockerfiles for optimized builds
- Docker Compose for orchestration
- Environment variable passing for secrets
- Volume mounting for persistent data

## Development Patterns & Best Practices

### Architecture Principles
1. **Clean Architecture** - Separation of concerns
2. **Dependency Injection** - Loose coupling between components
3. **Interface Segregation** - Fine-grained contracts
4. **Single Responsibility** - Each class has one reason to change

### Code Quality
1. **Async/Await** - Non-blocking I/O operations
2. **Logging** - Comprehensive diagnostic output
3. **Error Handling** - Graceful degradation
4. **Configuration** - Externalized settings

### Testing Approach
1. **Unit Testing** - Mock services for isolated testing
2. **Integration Testing** - Real services with controlled data
3. **API Testing** - HTTP endpoint validation
4. **UI Testing** - Manual validation of web interface

## Key Flows & Workflows

### Document Processing Workflow
1. **Scan** - Identify documents in configured directories
2. **Extract** - Parse content (PDF, text, web)
3. **Chunk** - Split into semantic chunks with overlap
4. **Embed** - Generate vector representations
5. **Store** - Save vectors with metadata to Qdrant

### Search Workflow
1. **Query** - Receive user question
2. **Embed** - Generate embedding for query
3. **Search** - Find similar vectors in Qdrant
4. **Rank** - Score results by similarity
5. **Contextualize** - Format top results as context
6. **Generate** - Send context+query to LLM
7. **Respond** - Return enhanced response with sources

### Conversation Management
1. **Create** - Initialize new chat session
2. **Track** - Store message history
3. **Context** - Include recent exchanges in RAG
4. **Persist** - Save conversations to database
5. **Retrieve** - Load history for continuation

This architecture provides a robust foundation for a privacy-focused knowledge assistant that can process various document types and provide intelligent, context-aware responses while maintaining complete control over data and processing.