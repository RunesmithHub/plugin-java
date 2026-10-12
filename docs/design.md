# The Java analyzer

The design of the Java analyzer, which runs on Runesmith's language services. The shared core it builds on, with its scheduler, request
pipeline and completion ranking, is described in Runesmith's own
[language services design](https://github.com/RunesmithHub/Runesmith/blob/main/docs/design/language-services.md).

## Design

The Java analyzer is Runesmith's own, written in C#. It needs no JVM and no external server, and it supports every Java version from 8 to
25, chosen per project.

### Versions

The parser accepts the syntax of every version, and the analyzer reports a feature the project's version does not have, as `javac
--release` does: "Records need Java 16 or later". The table drives both the diagnostics and which keywords completion offers.

| Feature | Since |
| --- | --- |
| Lambdas, method references, default methods | 8 |
| Modules (`module-info.java`) | 9 |
| `var` for local variables | 10 |
| `var` in lambda parameters | 11 |
| Switch expressions and `yield` | 14 |
| Text blocks | 15 |
| Records, pattern matching for `instanceof` | 16 |
| Sealed classes (`sealed`, `non-sealed`, `permits`) | 17 |
| Pattern matching for `switch`, record patterns | 21 |
| Unnamed variables and patterns (`_`) | 22 |
| Markdown documentation comments (`///`) | 23 |
| Module import declarations, compact source files and instance `main` methods, flexible constructor bodies | 25 |

Preview features of the newest version, such as primitive types in patterns, parse too, and are reported unless the project enables
preview features.

### Syntax

- **Lexer.** All of Java's tokens: Unicode escapes, every number form with underscores, char and string literals, text blocks with their
  indentation rules, comments (and documentation comments of both kinds), and the contextual keywords (`var`, `record`, `sealed`,
  `permits`, `yield`, `module`, `open`, `requires` and the others) as identifiers the parser interprets by position.
- **Parser.** A hand-written recursive descent parser with precedence climbing for expressions. It produces an immutable tree in which
  every node has its span, and keeps going after errors: a missing token is inserted, an unexpected one skipped, and statements and
  members resynchronize at `;`, `}` and the next member's start, so a half-typed line never hides the rest of the file.
- **Incremental reparsing.** After an edit, only the smallest enclosing member body (a method, constructor or initializer block) is
  reparsed when the edit stays inside it and its braces still balance; otherwise the whole file is.
- **Correctness.** Every source file of the JDK's own `src.zip` parses with no errors.

### The class library

- **The JDK.** The analyzer finds JDKs through `JAVA_HOME`, `java` on the `PATH`, and the usual install folders on each system. Its API for
  a given release comes from the JDK's `lib/ct.sym`, which holds the public API of every release from 8 to the JDK's own as class files, so
  a project targeting Java 11 is offered exactly Java 11's API, even with a newer JDK installed.
- **Class files.** A class file reader reads the constant pool, classes, fields, methods, generic signatures, inner classes and records
  and permitted subclasses, into compact symbols. It reads only what completion and resolution need, and only when first asked.
- **Libraries.** JARs referenced by the project, from the local Maven repository and the Gradle cache, are read the same way, and indexed
  once per file.

### Projects

- **Maven**: `pom.xml` with its parent chain for the source folders, `maven.compiler.release` (or `source`), preview flags and
  dependencies, resolved against `~/.m2/repository`, transitively through their POMs.
- **Gradle**: `build.gradle` and `build.gradle.kts`, read for the Java toolchain or `sourceCompatibility`, source sets and dependencies,
  found in the Gradle cache. The build scripts are read, not run.
- **Plain folders**: every `.java` file under the folder, with the newest Java version and the JDK's API.

### Semantics

- **Scopes and names**: packages, imports (single type, on demand, static, and module imports), nested and local types, members inherited
  from supertypes and interfaces, locals and parameters, with Java's shadowing rules.
- **Types**: declared types with generics; `var` inferred from its initializer; expression types for literals, `new`, field and method
  access (with type arguments substituted), calls of overloaded methods chosen by argument count and types, casts, conditional and switch
  expressions, and lambda parameters from the target functional interface where the target is known.
- **Diagnostics** are reported only where the analyzer is certain: syntax errors, features newer than the project's version, imports of
  types that do not exist, names that resolve to nothing in a fully understood scope. Anything it cannot fully resolve, it leaves alone
  rather than guess, so it never shows a problem that the compiler would not.

### Features

- **Completion**: keywords valid at the position and version, locals, parameters, fields and methods in scope, members after `.` (including
  static members of types and inherited members), types for declarations and `new`, packages and types in imports, and types that are not
  imported yet with the `import` added on accept.
- **Hover**: a symbol's signature and its documentation comment, from source or, for the JDK, the API's signature.
- **Go to definition**: declarations in the project's sources.
- **Signature help**: overloads of the called method or constructor and the active parameter.

## Performance goals

| Measurement | Target |
| --- | --- |
| Lexing | over 100 MB/s |
| Parsing a 10,000-line file from scratch | under 20 ms |
| Reparsing after typing inside a method | under 1 ms |
| Completion, member access and statement starts | p95 under 10 ms |
| Loading the JDK API for a release, first time and from the cache | under 500 ms and under 50 ms |
| Diagnostics of a 2,000-line file | under 30 ms |
| Every file of the JDK's `src.zip` | parses with no errors |

## Results

The analyzer's benchmarks run on a generated project of 150 classes; the editor's numbers come from typing into
`java.util.stream.Collectors` in a Maven project made of the JDK's own `java.util` sources.

| Measurement | Result | Target | Met |
| --- | --- | --- | --- |
| Lexing | 228 to 322 MB/s | over 100 MB/s | Yes |
| Parsing a 10,000-line file from scratch | 7.4 to 15.1 ms | under 20 ms | Yes |
| Reparsing after typing inside a method | 0.1 to 0.6 ms | under 1 ms | Yes |
| Completion, measured directly | p95 0.1 ms | p95 under 10 ms | Yes |
| Completion requests while typing in the editor | p95 11.7 ms | p95 under 10 ms | No |
| Loading the JDK API, first time and from the cache | 35 ms and 3.9 ms | under 500 ms and under 50 ms | Yes |
| Diagnostics of a 2,000-line file | 13.6 ms | under 30 ms | Yes |
| Every file of the JDK's `src.zip` | 15,227 files, no errors | no errors | Yes |
| Semantic problems reported in 490 correct JDK files | none | none | Yes |
