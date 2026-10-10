# Contributing

Thanks for helping. For a bug, open an issue with the steps to reproduce it; for a security problem, follow [SECURITY.md](SECURITY.md)
instead. For a larger change, open an issue first, so the approach is agreed on before you write the code.

## Making a change

1. Fork the repository and create a branch from `main`.
2. Build and test with the .NET SDK version in `global.json`. The Java analyzer is part of this repository, in
   `src/Runesmith.Languages.Java.Syntax` and `src/Runesmith.Languages.Java`, so changes to it are made here too:

   ```sh
   dotnet build plugin-java.slnx -warnaserror
   dotnet format plugin-java.slnx --verify-no-changes
   dotnet test --solution plugin-java.slnx
   ```

3. Check the plugin as the hub does when it publishes a release, with the hub tool:

   ```sh
   dotnet tool install --global RunesmithHub.Tool --version 0.1.2
   rshub check .
   ```

4. Write each commit message as a [Conventional Commit](https://www.conventionalcommits.org/en/v1.0.0/), such as
   `fix: keep the selected JDK after a refresh`. The types are `feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `ci`, `chore` and
   `revert`.
5. Open a pull request against `main`.

## License

This plugin is licensed under the [Apache License 2.0](LICENSE). Contributions you submit are licensed under the same terms, as section 5
of the license describes.
