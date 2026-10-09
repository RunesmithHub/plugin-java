# Java for Runesmith

The official Java plugin for [Runesmith](https://github.com/RunesmithHub/Runesmith), `runesmith.java`. It adds:

- Java highlighting, completion, signature help, hover, go to definition and problems, from Runesmith's own Java analyzer, which runs
  inside Runesmith and needs no JVM or language server.
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

You need the .NET SDK version in `global.json`.

```sh
dotnet build plugin-java.slnx -warnaserror
dotnet test --solution plugin-java.slnx
```

`dotnet build src/Runesmith.Java -t:InstallPlugin` installs your build into your Runesmith plugins folder; restart Runesmith to load it.

## Documentation

[Java support](https://runesmithhub.github.io/Runesmith/plugins/java) in the
[Runesmith documentation](https://runesmithhub.github.io/Runesmith/) describes every feature, setting and limit.

## License

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
