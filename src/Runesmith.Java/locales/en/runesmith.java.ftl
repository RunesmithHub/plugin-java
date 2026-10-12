### The Java plugin's text in English, the source every translation starts from. The build turns each message into a member of the
### Strings class, such as Strings.RunApplicationName.

## The kinds of run configuration, as Run › Edit Configurations lists them.

run-application-name = Java application
run-application-description = Run a class with a main method, or a compact source file.
run-maven-name = Maven goals
run-maven-description = Run Maven goals, such as clean install, with profiles.
run-gradle-name = Gradle tasks
run-gradle-description = Run Gradle tasks, such as build or run.
run-junit-name = JUnit tests
run-junit-description = Run the JUnit tests of a Maven or Gradle project, or the ones a filter picks.

## The fields of a run configuration.

option-main-class = Main class
option-main-class-description = The class with the main method, or the path of a compact source file such as Hello.java.
option-jdk-description = The JDK that runs the program; the default JDK when none is chosen.
option-vm-options = VM options
option-vm-options-description = Options for the JVM, one per line.
option-program-arguments = Program arguments
option-program-arguments-description = Arguments passed to main, one per line.
option-module = Module
option-module-description = The module of the main class, which then runs from the module path.
option-arguments = Arguments
# Shown in an empty row of a list of arguments.
option-arguments-placeholder = Argument
option-environment = Environment variables
option-working-directory = Working directory
# Shown in the empty field: what is used when no folder is chosen.
option-working-directory-placeholder = The project's folder
# Maven goals, such as clean install.
option-goals = Goals
option-profiles = Profiles
option-profiles-description = The profiles to activate, separated by commas.
option-maven-arguments-description = More arguments for Maven, one per line, such as -DskipTests.
# Gradle tasks, such as clean build.
option-tasks = Tasks
option-gradle-arguments-description = More arguments for Gradle, one per line, such as --info.
# Which tests to run.
option-tests = Tests
# Shown in the empty field: every test runs.
option-tests-placeholder = All tests
option-tests-description = A test class, a method such as MathTest#adds, or a pattern such as *IntegrationTest; empty runs every test.
option-build-tool-arguments-description = More arguments for the build tool, one per line.

## What stops a run configuration from starting. $configuration is the configuration's name.

run-no-main-class = { $configuration } has no main class.
run-no-build-tool = JUnit tests run through Maven or Gradle, and the folder has neither a pom.xml nor a Gradle build.

## The JDKs in Settings › SDKs.

jdk = JDK
# Shown under the title of the dialog that downloads a JDK.
jdk-source = Builds of OpenJDK from their vendors: Temurin from Adoptium, the others as the foojay Discovery API lists them.
# A JDK that also has the JavaFX libraries.
jdk-with-javafx = JDK with JavaFX
# The steps of installing a JDK, shown next to the progress bar.
progress-preparing = Preparing
progress-downloading = Downloading
progress-verifying = Verifying
progress-unpacking = Unpacking
progress-installing = Installing
# $vendor is the distribution's name, such as Temurin; $version is the JDK's version, such as 25.0.1.
jdk-no-download = There is no download for { $vendor } { $version }.
jdk-download-incomplete = The download of { $vendor } { $version } does not contain a JDK.
jdk-not-managed = Runesmith did not install the JDK at { $path }, so it does not remove it.
# $algorithm is the checksum's name, such as SHA256.
jdk-checksum-mismatch = The download does not match its published { $algorithm } checksum, so it was not installed.
