## convert-to-linq-call-form

Convert a foreach loop into a LINQ expression using fluent method calls (Where/Select)

```json
{
  "type": "object",
  "properties": {
    "project": {
      "type": "string",
      "description": "Path to a .sln or .csproj file"
    },
    "file": {
      "type": "string",
      "description": "Path to the file containing the selection"
    },
    "start-line": {
      "type": "integer",
      "description": "1-based start line of the selection"
    },
    "start-column": {
      "type": "integer",
      "description": "1-based start column of the selection"
    },
    "end-line": {
      "type": "integer",
      "description": "1-based end line of the selection"
    },
    "end-column": {
      "type": "integer",
      "description": "1-based end column of the selection"
    }
  },
  "required": [
    "project",
    "file",
    "start-line",
    "start-column",
    "end-line",
    "end-column"
  ]
}
```

## convert-to-linq-query-form

Convert a foreach loop into a LINQ expression using query syntax (from/where/select)

```json
{
  "type": "object",
  "properties": {
    "project": {
      "type": "string",
      "description": "Path to a .sln or .csproj file"
    },
    "file": {
      "type": "string",
      "description": "Path to the file containing the selection"
    },
    "start-line": {
      "type": "integer",
      "description": "1-based start line of the selection"
    },
    "start-column": {
      "type": "integer",
      "description": "1-based start column of the selection"
    },
    "end-line": {
      "type": "integer",
      "description": "1-based end line of the selection"
    },
    "end-column": {
      "type": "integer",
      "description": "1-based end column of the selection"
    }
  },
  "required": [
    "project",
    "file",
    "start-line",
    "start-column",
    "end-line",
    "end-column"
  ]
}
```

## extract-method

Extract selected statements into a new method

```json
{
  "type": "object",
  "properties": {
    "project": {
      "type": "string",
      "description": "Path to a .sln or .csproj file"
    },
    "file": {
      "type": "string",
      "description": "Path to the file containing the selection"
    },
    "start-line": {
      "type": "integer",
      "description": "1-based start line of the selection"
    },
    "start-column": {
      "type": "integer",
      "description": "1-based start column of the selection"
    },
    "end-line": {
      "type": "integer",
      "description": "1-based end line of the selection"
    },
    "end-column": {
      "type": "integer",
      "description": "1-based end column of the selection"
    }
  },
  "required": [
    "project",
    "file",
    "start-line",
    "start-column",
    "end-line",
    "end-column"
  ]
}
```

## inline-method

Inline a called method's (or local function's) body at the call site

```json
{
  "type": "object",
  "properties": {
    "project": {
      "type": "string",
      "description": "Path to a .sln or .csproj file"
    },
    "file": {
      "type": "string",
      "description": "Path to the file containing the call site"
    },
    "start-line": {
      "type": "integer",
      "description": "1-based start line of the selection"
    },
    "start-column": {
      "type": "integer",
      "description": "1-based start column of the selection"
    },
    "end-line": {
      "type": "integer",
      "description": "1-based end line of the selection"
    },
    "end-column": {
      "type": "integer",
      "description": "1-based end column of the selection"
    }
  },
  "required": [
    "project",
    "file",
    "start-line",
    "start-column",
    "end-line",
    "end-column"
  ]
}
```

## inline-temporary-variable

Inline a local variable's initializer into all usages, then remove the declaration

```json
{
  "type": "object",
  "properties": {
    "project": {
      "type": "string",
      "description": "Path to a .sln or .csproj file"
    },
    "file": {
      "type": "string",
      "description": "Path to the file containing the selection"
    },
    "start-line": {
      "type": "integer",
      "description": "1-based start line of the selection"
    },
    "start-column": {
      "type": "integer",
      "description": "1-based start column of the selection"
    },
    "end-line": {
      "type": "integer",
      "description": "1-based end line of the selection"
    },
    "end-column": {
      "type": "integer",
      "description": "1-based end column of the selection"
    }
  },
  "required": [
    "project",
    "file",
    "start-line",
    "start-column",
    "end-line",
    "end-column"
  ]
}
```

## introduce-variable

Introduce a local variable for a selected expression

```json
{
  "type": "object",
  "properties": {
    "project": {
      "type": "string",
      "description": "Path to a .sln or .csproj file"
    },
    "file": {
      "type": "string",
      "description": "Path to the file containing the selection"
    },
    "start-line": {
      "type": "integer",
      "description": "1-based start line of the selection"
    },
    "start-column": {
      "type": "integer",
      "description": "1-based start column of the selection"
    },
    "end-line": {
      "type": "integer",
      "description": "1-based end line of the selection"
    },
    "end-column": {
      "type": "integer",
      "description": "1-based end column of the selection"
    }
  },
  "required": [
    "project",
    "file",
    "start-line",
    "start-column",
    "end-line",
    "end-column"
  ]
}
```

## move-static-member

Move a static member (method/property/field/event) to another type in the same project

```json
{
  "type": "object",
  "properties": {
    "project": {
      "type": "string",
      "description": "Path to a .sln or .csproj file"
    },
    "file": {
      "type": "string",
      "description": "Path to the file containing the member"
    },
    "line": {
      "type": "integer",
      "description": "1-based line of the member"
    },
    "column": {
      "type": "integer",
      "description": "1-based column of the member"
    },
    "to": {
      "type": "string",
      "description": "Fully qualified name of the destination type (must already exist in the same project)"
    }
  },
  "required": [
    "project",
    "file",
    "line",
    "column",
    "to"
  ]
}
```

## rename

Rename a symbol across a solution/project

```json
{
  "type": "object",
  "properties": {
    "project": {
      "type": "string",
      "description": "Path to a .sln or .csproj file"
    },
    "file": {
      "type": "string",
      "description": "Path to the file containing the symbol"
    },
    "line": {
      "type": "integer",
      "description": "1-based line of the symbol"
    },
    "column": {
      "type": "integer",
      "description": "1-based column of the symbol"
    },
    "to": {
      "type": "string",
      "description": "The new name for the symbol"
    }
  },
  "required": [
    "project",
    "file",
    "line",
    "column",
    "to"
  ]
}
```

