# Java in the workbench

What the plugin adds to Runesmith's run configurations, New Project dialog and SDKs page, whose design is in Runesmith's own
[workbench design](https://github.com/RunesmithHub/Runesmith/blob/main/docs/design/workbench.md).

## Run configurations

Application (main class and module, JDK, VM options, program arguments, class path from the project), Maven goals and Gradle
  tasks, and JUnit tests.

## SDKs

Distributions and packages come from the foojay Discovery API, which lists builds of OpenJDK from many
  vendors (Temurin, Zulu, Corretto, Liberica, Microsoft, GraalVM and others) with their checksums. The download form filters by vendor,
  major version (marked LTS where it is), package (JDK or JDK with JavaFX) and architecture. Found JDKs come from `JAVA_HOME`, the `java`
  on the `PATH`, `/usr/lib/jvm`, `~/.jdks`, `~/.sdkman/candidates/java` and the platform's usual folders. Projects pick a JDK, and the Java
  analyzer reads that JDK's API for the project's release.

## Templates

The templates are in their own plugin, `runesmith.java-templates`, so a user can switch them off without losing the language. It
has Runesmith's own Java templates: Application, Library, Compact source file, and Multi-module
  project. Their options: build system (Maven, Gradle or none), Gradle's script language (Kotlin or Groovy), group and artifact, JDK,
  Java release, module declaration, JUnit tests and sample code.
