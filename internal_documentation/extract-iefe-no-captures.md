# extract-iefe-no-captures

## What it does

Takes a selected run of statements, or a single expression (the same two kinds of
selection `extract-method` takes), and wraps it in a static lambda that is immediately
invoked in place — an "immediately-invoked function expression" (IIFE) — instead of
extracting it to a separate, reusable, named method. The wrapper is always a lambda,
never a local function, for both selection kinds - see "Always a lambda, never a local
function" below for why a lambda literal needs a cast to be invocable at all.

Anything the selection references from the enclosing scope — an outer local, a
parameter, or an implicit `this`/instance member — becomes an explicit parameter of the
wrapped function instead of blocking the extraction. That's what makes the wrapper a
genuine `static` function with no closure: nothing is captured, because everything the
selection needs is threaded through its parameter list, and the call site passes the
current value of each one as an argument.

### before

```csharp
void Render()
{
    var header = BuildHeader();
    var multiplier = 2;

    // begin selection
    var items = new List<string>();
    foreach (var x in Enumerable.Range(0, 10))
    {
        items.Add((x * multiplier).ToString());
    }
    // end selection

    Console.WriteLine(header);
    Console.WriteLine(string.Join(",", items));
}
```

### after

```csharp
void Render()
{
    var header = BuildHeader();
    var multiplier = 2;

    var items = ((Func<int, List<string>>)(static (multiplier) =>
    {
        var items = new List<string>();
        foreach (var x in Enumerable.Range(0, 10))
        {
            items.Add((x * multiplier).ToString());
        }
        return items;
    }))(multiplier);

    Console.WriteLine(header);
    Console.WriteLine(string.Join(",", items));
}
```

`multiplier` is read inside the selection but declared outside it, so it becomes a
parameter of the lambda, passed the current value at the call site. `header` isn't
referenced by the selection at all, so it's left alone. A selection that *writes* to an
outer variable (not just reads it) can't be handled this way — see "Failure modes" below.

It also accepts a single-expression selection (marked with `/*[*/` and `/*]*/` around the
expression span, no `begin selection`/`end selection` comments needed), the same way
`extract-method` can extract an expression instead of a statement run:

### before

```csharp
void Render(IEnumerable<int> x)
{
    var averagePlusOne = /*[*/x.Sum() / x.Count()/*]*/ + 1;
}
```

### after

```csharp
void Render(IEnumerable<int> x)
{
    var averagePlusOne = ((Func<IEnumerable<int>, int>)(static (x) => x.Sum() / x.Count()))(x) + 1;
}
```

Here `x` is a parameter of the enclosing method, read (not written) inside the selected
expression, so it becomes a parameter of the lambda and is passed the current value as an
argument. Note the delegate cast has to be `Func<IEnumerable<int>, int>`, not just
`Func<int>` — the lambda's parameter list is part of its type, so the cast target has to
match it exactly, one type argument per parameter plus the return type.

For a selected expression the IIFE is always expression-bodied — there's no block of
statements to decide a return from, so the lambda body is simply `=> <expression>` and
the whole thing collapses to a single statement/expression at the call site. This is a
formatting nicety on top of the same mechanism, not a separate analysis: the same
data-flow analysis applies to the expression's descendant nodes.

`this` and instance members work the same way as outer locals/parameters, but collapse
to a single parameter rather than one per member: if the selection implicitly or
explicitly references `this` (any unqualified instance member access, or `base` access),
the wrapper gains one receiver parameter of the containing type (named the way
`make-method-static` names its receiver parameter - see "Relationship to
make-method-static" below), and every implicit member access inside the selection is
rewritten to go through it, e.g. `_count` becomes `self._count`.

A generic method's own type parameters need no special handling at all: a `static`
lambda nested inside a generic method already has access to the enclosing method's type
parameters directly, without needing them passed in or repeated on the lambda itself -
they're a compile-time concept shared by every nested scope, not something a closure
captures at runtime. `typeof(T)`, `default(T)`, `new List<T>()`, etc. all just work
unmodified inside the wrapped code.

### Always a lambda, never a local function

C# doesn't allow a lambda literal to be invoked directly - `(() => x)()` fails to
compile with `CS0149` ("method name expected"), because postfix invocation only applies
to primary expressions and a bare lambda literal isn't one, regardless of its parameter
list. Every example above works around this the same way: casting the lambda to an
explicit delegate type first (`(Func<...>)(...)`), which turns it into a primary
expression, then wrapping *that* in another layer of parens before invoking it - a cast
binds looser than postfix invocation, so `(T)x()` means `(T)(x())`, not `((T)x)()`. The
result is always `((DelegateType)(static (...) => ...))(...)`, whether the selection is a
statement span or an expression, and whether or not it takes any parameters. There's no
separate "local function" output form to choose between - a local function was the
obvious escape from this problem for a statement-span selection (it's just a named
method, no cast needed to call it), but this command doesn't take that path for either
selection kind, so both go through the same cast-and-invoke shape.

## Why this is a custom refactoring

There's no Roslyn CodeAction that does this — Roslyn's extract-method family always
produces a persistent, separately-named, separately-located method or local function
meant to be called from more than one place conceptually (even if only used once). This
command is narrower and has a different purpose: it's a *scoping* tool, not a *reuse*
tool. It lets a long method be broken into visually distinct, independently-readable
chunks — each with its own temporary variables that don't leak into the surrounding
scope — without the ceremony of naming a new method, deciding where it lives in the
type, or exposing it as something callable from elsewhere. Turning captures into
parameters means its data-flow analysis overlaps with extract-method's, but the output
doesn't: nothing is relocated out of the enclosing method, given its own name in the
type's member list, or made independently discoverable/callable - it stays exactly where
it was, as an inline implementation detail of the method the selection came from.

## Design

### Selection and preconditions

- Selection is either a statement span or a single expression within a
  method/local-function/lambda body, resolved via
  `start-line`/`start-column`/`end-line`/`end-column` exactly like `extract-method`.
- A statement-span selection must form one or more complete statements (no partial
  expressions); an expression selection must resolve to a single complete expression
  node (no partial subexpression, no expression that straddles multiple statements) —
  the same two requirements `extract-method` has for its two selection kinds.
- **Capture analysis** (the command's core mechanism): using the `SemanticModel`'s data
  flow analysis (`AnalyzeDataFlow` — the same API `extract-method`'s underlying provider
  uses to determine captured variables and flow), find everything the selection
  references from the enclosing scope and turn each into a parameter instead of a
  closure:
  - a local or parameter declared outside the selection that's *read* inside it becomes
    an ordinary by-value parameter, passed the current value at the call site;
  - one that's *written* inside the selection can't be supported — see "Failure modes to
    report clearly" below;
  - an implicit or explicit reference to `this` (any unqualified instance member access,
    or `base` access) collapses to a single receiver parameter of the containing type,
    reused for every such reference in the selection - not one parameter per member
    accessed;
  - a reference to an enclosing generic method's own type parameter needs nothing at all
    (see "What it does" above) — it was never actually a capture.

  Each new parameter's name is reused from the captured local/parameter's own name (or,
  for the receiver parameter, chosen the way `make-method-static` names its receiver -
  see below); parameters are ordered by where each is first referenced in the selection.

### Determining the "return"

This only applies to a statement-span selection — an expression selection has an
obvious, unambiguous return (the expression's own value), so this analysis is skipped
entirely and the IIFE body is just `=> <expression>`.

- For a statement-span selection, data flow analysis also gives the set of variables
  declared *inside* the selection that are used *after* it
  (`DataFlowAnalysis.DataFlowsOut`, filtered to variables the selection itself declares).
  - Zero such variables: the IIFE is `void`-returning, wrapped in an `Action` cast;
    statement becomes `((Action<...>)(static (...) => { ... }))(...);`.
  - Exactly one: the IIFE returns it; statement becomes
    `var <name> = ((Func<..., T>)(static (...) => { ...; return <name>; }))(...);`,
    using the existing variable's name and declaring it as usual on assignment.
  - More than one: return a tuple. The lambda's return type becomes a named-element
    tuple built from the variables' own names, e.g. `(int count, int total)`, and the
    call site destructures it back into locals with those same names:

    ```csharp
    var (count, total) = ((Func<(int count, int total)>)(static () =>
    {
        var count = 0;
        var total = 0;
        foreach (var i in Enumerable.Range(0, 5))
        {
            count++;
            total += i;
        }
        return (count, total);
    }))();
    ```

### Failure modes to report clearly

- Selection is not a set of complete statements, or not a single complete expression.
- Selection writes to a captured outer local/parameter. A write needs a `ref` parameter
  to be observed by the caller, and there's no way to express that here: the wrapper is
  always cast to a plain `Func<>`/`Action<>` (there's no `Func<ref int, ...>`), and this
  command doesn't declare a custom delegate type or fall back to a local function for
  this case (see "Always a lambda, never a local function" above) - so a write to
  anything outside the selection is rejected outright, naming the offending variable.
- Selection contains a `yield`, or an `await` under a synchronization context that would
  behave differently once moved inside a lambda (mirrors the same caution
  `extract-method` already has to take — reuse its handling rather than reinventing it).

## Relationship to make-method-static

This command reuses the same idea `make-method-static` uses for `this` — replace an
implicit dependency on the enclosing context with an explicit parameter, so the compiler
can guarantee `static` really means static — and applies it to every capture a selection
might have (outer locals and parameters, not just the receiver), scoped to a selection
within a method rather than the method's entire body. The receiver-parameter mechanics
for `this` (naming, rewriting implicit member accesses to go through it) are literally
the same logic `make-method-static` already has for its whole-method version; see
`make-method-static.md` for the naming/collision rules it follows.
