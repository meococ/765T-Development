#!/usr/bin/env python3
"""
Mem0 MCP Server — Self-hosted memory layer for BIM765T.

Exposes Mem0's persistent memory as MCP tools over stdio.
Uses local Qdrant (if available) or in-memory fallback.

Tools:
  - mem0_add: Store memories from conversation
  - mem0_search: Semantic search across memories
  - mem0_get_all: List all memories for a user/agent
  - mem0_delete: Remove a specific memory
  - mem0_history: Get evolution history of a memory
"""

import json
import sys
import os
from datetime import datetime

# Mem0 config — self-hosted with Qdrant or in-memory fallback
MEM0_CONFIG = {
    "vector_store": {
        "provider": "qdrant",
        "config": {
            "host": os.environ.get("QDRANT_HOST", "localhost"),
            "port": int(os.environ.get("QDRANT_PORT", 6333)),
            "collection_name": os.environ.get("MEM0_COLLECTION", "bim765t_mem0"),
        },
    },
    "llm": {
        "provider": os.environ.get("MEM0_LLM_PROVIDER", "openai"),
        "config": {
            "model": os.environ.get("MEM0_LLM_MODEL", "gpt-4.1-nano"),
            "temperature": 0.1,
            "max_tokens": 2000,
        },
    },
    "version": "v1.1",
}

# Fallback: if no Qdrant, use in-memory
MEM0_CONFIG_FALLBACK = {
    "vector_store": {
        "provider": "chroma",
        "config": {
            "collection_name": "bim765t_mem0",
            "path": os.path.join(
                os.environ.get("APPDATA", os.path.expanduser("~")),
                "BIM765T.Revit.Agent",
                "mem0_chroma",
            ),
        },
    },
    "llm": {
        "provider": os.environ.get("MEM0_LLM_PROVIDER", "openai"),
        "config": {
            "model": os.environ.get("MEM0_LLM_MODEL", "gpt-4.1-nano"),
            "temperature": 0.1,
            "max_tokens": 2000,
        },
    },
    "version": "v1.1",
}

_memory = None


def get_memory():
    """Lazy init Mem0 Memory instance."""
    global _memory
    if _memory is not None:
        return _memory

    from mem0 import Memory

    try:
        _memory = Memory.from_config(MEM0_CONFIG)
        log("Mem0 initialized with Qdrant backend")
    except Exception as e:
        log(f"Qdrant unavailable ({e}), falling back to ChromaDB local")
        try:
            _memory = Memory.from_config(MEM0_CONFIG_FALLBACK)
            log("Mem0 initialized with ChromaDB fallback")
        except Exception as e2:
            log(f"ChromaDB also failed ({e2}), using bare in-memory")
            _memory = Memory()
            log("Mem0 initialized with default in-memory")

    return _memory


# --- JSON-RPC over stdio ---

def log(msg):
    """Log to stderr (visible in Claude Code MCP logs)."""
    sys.stderr.write(f"[mem0-mcp] {msg}\n")
    sys.stderr.flush()


def send_response(id, result=None, error=None):
    """Send JSON-RPC response."""
    resp = {"jsonrpc": "2.0", "id": id}
    if error:
        resp["error"] = error
    else:
        resp["result"] = result
    line = json.dumps(resp)
    sys.stdout.write(line + "\n")
    sys.stdout.flush()


def send_notification(method, params=None):
    """Send JSON-RPC notification."""
    msg = {"jsonrpc": "2.0", "method": method}
    if params:
        msg["params"] = params
    sys.stdout.write(json.dumps(msg) + "\n")
    sys.stdout.flush()


TOOLS = [
    {
        "name": "mem0_add",
        "description": "Store memories from a conversation. Mem0 auto-extracts key facts, deduplicates, and maintains a knowledge graph. Use for: saving user preferences, project decisions, lessons learned, BIM standards context.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "messages": {
                    "type": "array",
                    "description": "Conversation messages to extract memories from. Each item: {role: 'user'|'assistant', content: '...'}",
                    "items": {
                        "type": "object",
                        "properties": {
                            "role": {"type": "string"},
                            "content": {"type": "string"},
                        },
                        "required": ["role", "content"],
                    },
                },
                "user_id": {
                    "type": "string",
                    "description": "User identifier (default: 'bim765t_user')",
                },
                "agent_id": {
                    "type": "string",
                    "description": "Agent identifier (default: 'bim765t_agent')",
                },
                "metadata": {
                    "type": "object",
                    "description": "Optional metadata (project_name, discipline, etc.)",
                },
            },
            "required": ["messages"],
        },
    },
    {
        "name": "mem0_search",
        "description": "Semantic search across stored memories. Returns top relevant memories with scores. Use before planning to recall user preferences, past decisions, project context.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Search query in natural language",
                },
                "user_id": {
                    "type": "string",
                    "description": "Filter by user (default: 'bim765t_user')",
                },
                "agent_id": {
                    "type": "string",
                    "description": "Filter by agent",
                },
                "limit": {
                    "type": "integer",
                    "description": "Max results (default: 5)",
                },
            },
            "required": ["query"],
        },
    },
    {
        "name": "mem0_get_all",
        "description": "List all memories for a user/agent. Useful for context loading at session start.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "user_id": {
                    "type": "string",
                    "description": "User identifier (default: 'bim765t_user')",
                },
                "agent_id": {
                    "type": "string",
                    "description": "Agent identifier",
                },
            },
        },
    },
    {
        "name": "mem0_delete",
        "description": "Delete a specific memory by ID.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "memory_id": {
                    "type": "string",
                    "description": "The memory ID to delete",
                },
            },
            "required": ["memory_id"],
        },
    },
    {
        "name": "mem0_history",
        "description": "Get the evolution history of a specific memory (how it changed over time).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "memory_id": {
                    "type": "string",
                    "description": "The memory ID to get history for",
                },
            },
            "required": ["memory_id"],
        },
    },
]


def handle_initialize(id, params):
    """Handle MCP initialize handshake."""
    send_response(id, {
        "protocolVersion": "2024-11-05",
        "capabilities": {
            "tools": {"listChanged": False},
        },
        "serverInfo": {
            "name": "mem0-bim765t",
            "version": "1.0.0",
        },
    })


def handle_tools_list(id, params):
    """Return available tools."""
    send_response(id, {"tools": TOOLS})


def handle_tool_call(id, params):
    """Execute a tool."""
    name = params.get("name", "")
    args = params.get("arguments", {})

    try:
        memory = get_memory()

        if name == "mem0_add":
            messages = args["messages"]
            user_id = args.get("user_id", "bim765t_user")
            agent_id = args.get("agent_id", "bim765t_agent")
            metadata = args.get("metadata", {})
            metadata["timestamp"] = datetime.utcnow().isoformat()

            result = memory.add(
                messages,
                user_id=user_id,
                agent_id=agent_id,
                metadata=metadata,
            )
            send_response(id, {
                "content": [{"type": "text", "text": json.dumps(result, default=str)}],
            })

        elif name == "mem0_search":
            query = args["query"]
            user_id = args.get("user_id", "bim765t_user")
            limit = args.get("limit", 5)
            kwargs = {"query": query, "user_id": user_id, "limit": limit}
            if "agent_id" in args:
                kwargs["agent_id"] = args["agent_id"]

            results = memory.search(**kwargs)
            send_response(id, {
                "content": [{"type": "text", "text": json.dumps(results, default=str)}],
            })

        elif name == "mem0_get_all":
            user_id = args.get("user_id", "bim765t_user")
            kwargs = {"user_id": user_id}
            if "agent_id" in args:
                kwargs["agent_id"] = args["agent_id"]

            results = memory.get_all(**kwargs)
            send_response(id, {
                "content": [{"type": "text", "text": json.dumps(results, default=str)}],
            })

        elif name == "mem0_delete":
            memory_id = args["memory_id"]
            memory.delete(memory_id)
            send_response(id, {
                "content": [{"type": "text", "text": f"Deleted memory {memory_id}"}],
            })

        elif name == "mem0_history":
            memory_id = args["memory_id"]
            history = memory.history(memory_id)
            send_response(id, {
                "content": [{"type": "text", "text": json.dumps(history, default=str)}],
            })

        else:
            send_response(id, error={"code": -32601, "message": f"Unknown tool: {name}"})

    except Exception as e:
        log(f"Tool error: {e}")
        send_response(id, {
            "content": [{"type": "text", "text": f"Error: {str(e)}"}],
            "isError": True,
        })


def main():
    """Main stdio JSON-RPC loop."""
    log("Starting Mem0 MCP server (self-hosted mode)")

    handlers = {
        "initialize": handle_initialize,
        "notifications/initialized": lambda id, p: None,
        "tools/list": handle_tools_list,
        "tools/call": handle_tool_call,
    }

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        try:
            msg = json.loads(line)
        except json.JSONDecodeError:
            continue

        method = msg.get("method", "")
        id = msg.get("id")
        params = msg.get("params", {})

        handler = handlers.get(method)
        if handler:
            handler(id, params)
        elif id is not None:
            send_response(id, error={"code": -32601, "message": f"Method not found: {method}"})


if __name__ == "__main__":
    main()
