# Java for Runesmith

The official Java plugin for [Runesmith](https://github.com/RunesmithHub/Runesmith), `runesmith.java`. It adds:

- Java highlighting, completion, signature help, hover, go to definition and problems, from the plugin's own Java analyzer, which runs
  inside Runesmith and needs no JVM or language server.
- Completion that inserts the parentheses a call needs, after a method's name or a type after `new`, with the caret between them when the
  call takes arguments, and a diamond for generic types.
- Find references, go to implementation, the occurrences of the name under the caret, the outline and breadcrumbs, go to symbol in the
  file and in the folder, folding, expand selection, call and type hierarchies, and names colored by what they mean.
- Building with Maven or Gradle, with compiler errors in the Problems panel.
- Run configurations for Java applications, Maven goals, Gradle tasks and JUnit tests.
- Finding the JDKs on your computer, and downloading new ones from Temurin, Zulu, Corretto, Liberica and other distributions.

Project templates come from a separate plugin, [Java templates](https://github.com/RunesmithHub/plugin-java-templates).

## Capabilities

| Capability | Why |
| --- | --- |
| `network` | Lists JDK distributions and downloads the JDKs you choose to install. |
| `process` | Runs Maven, Gradle and java to build, run and test your projects. |
| `filesystem` | Reads your projects' build files and keeps the JDKs it installs. |
| `environment` | Finds your JDK through `JAVA_HOME`. |

## Install

Install **Java** from the plugin manager in Runesmith, or from [the plugin hub](https://hub.runesmith.dev).

## Build and test

The Java analyzer lives in this repository: `src/Runesmith.Languages.Java.Syntax` is the lexer and parser, and
`src/Runesmith.Languages.Java` the semantics, the JDK and library class reader, and Maven and Gradle projects. Their tests are in
`tests`; some read a JDK 25 or newer, found through `JAVA_HOME` or the `PATH`, and skip without one.

You need the .NET SDK version in `global.json`.

```sh
dotnet build plugin-java.slnx -warnaserror
dotnet test --solution plugin-java.slnx
```

`dotnet build src/Runesmith.Java -t:InstallPlugin` installs your build into your Runesmith plugins folder; restart Runesmith to load it.

## Translations

The plugin's text is in English in `src/Runesmith.Java/locales/en/runesmith.java.ftl`, a [Fluent](https://projectfluent.org) file. Text
for a new message goes there, and the code reads it from the generated `Strings` class. A language pack translates the file; see
[Language packs](https://docs.runesmith.dev/plugins/language-packs). The problems the analyzer reports are in English.

## Documentation

[Java support](https://docs.runesmith.dev/plugins/java) in the
[Runesmith documentation](https://docs.runesmith.dev/) describes every feature, setting and limit.

## License

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
