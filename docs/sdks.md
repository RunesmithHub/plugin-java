# JDKs

What the Java plugin adds to Runesmith's **SDKs** page; the page itself is described in
[SDKs](https://docs.runesmith.dev/guide/sdks).

## The default

The Java analyzer reads the Java API from the default JDK when it serves the project's release, and from the newest JDK that does
otherwise.

## Downloads

Pick the **Vendor**, the **Version**, the **Package** (**JDK**, or **JDK with JavaFX** where the vendor has one), the **Architecture** and
the **Release**. The dialog starts at Temurin. Releases come from the foojay Discovery API, which lists OpenJDK builds from Temurin, Zulu,
Corretto, Liberica, Microsoft, Oracle OpenJDK, GraalVM Community, SapMachine and Semeru. Runesmith installs each JDK in a folder of its
own, `sdks/jdk/<vendor>-<version>`.

An update is a newer version of the same vendor and major version. Removing a JDK deletes its folder.

## Where the plugin looks

Runesmith's own JDK folder, `JAVA_HOME`, the JDK of the `java` on the `PATH` (following links), `~/.sdkman/candidates/java`, `~/.jdks`,
`~/.gradle/jdks`, `/usr/lib/jvm` on Linux, `/Library/Java/JavaVirtualMachines` on macOS, and the vendors' folders in `Program Files` on
Windows, such as `Java` and `Eclipse Adoptium`. A JDK's version and vendor come from its `release` file.
