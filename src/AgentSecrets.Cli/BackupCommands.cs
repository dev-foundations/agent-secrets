namespace AgentSecrets.Cli;

public static class BackupCommands
{
    public static int Export(string[] args, CliContext context)
    {
        string path = ParsePath(args, context, importing: false, out _);
        RequireTerminal(context, "export");
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new BackupException("The destination already exists. Choose a new backup filename; existing files are never overwritten.");
        }

        IReadOnlyList<string> names = context.Store.List();
        if (names.Count == 0)
        {
            throw new BackupException("There are no secrets to export.");
        }

        context.Out.WriteLine($"Exporting {names.Count} secret(s) to a password-encrypted backup. Use a long, unique passphrase and keep it safe; it cannot be recovered.");
        string password = ReadPassword(context, "Backup password: ");
        string confirmation = ReadPassword(context, "Confirm password: ");
        if (!password.Equals(confirmation, StringComparison.Ordinal))
        {
            throw new BackupException("The passwords do not match. Nothing was exported.");
        }

        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string name in names)
            {
                secrets.Add(name, context.Store.Get(name)
                    ?? throw new BackupException("A secret disappeared during export. Nothing was exported; try again."));
            }

            byte[] archive = EncryptedBackup.Encrypt(secrets, password);
            WriteNewFile(path, archive);
        }
        finally
        {
            secrets.Clear();
        }

        context.Out.WriteLine($"Exported {names.Count} secret(s). The backup contains encrypted aliases and values; no plaintext was written.");
        return ExitCodes.Success;
    }

    public static int Import(string[] args, CliContext context)
    {
        string path = ParsePath(args, context, importing: true, out bool force);
        RequireTerminal(context, "import");
        byte[] archive;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length > EncryptedBackup.MaxFileBytes)
            {
                throw new BackupException("The backup exceeds the 16 MiB size limit. Nothing was imported.");
            }

            archive = new byte[(int)stream.Length];
            stream.ReadExactly(archive);
            if (stream.ReadByte() != -1)
            {
                throw new BackupException("The backup changed while it was read. Nothing was imported; try again.");
            }
        }

        string password = ReadPassword(context, "Backup password: ");
        Dictionary<string, string> secrets = EncryptedBackup.Decrypt(archive, password);
        int imported = 0;
        try
        {
            // Authenticate and validate the whole file, then check every conflict before any write.
            foreach (var (name, value) in secrets)
            {
                if (OperatingSystem.IsWindows() && value.Length > WindowsCredentialStore.MaxSecretChars)
                {
                    throw new BackupException($"Secret '{name}' exceeds Windows Credential Manager's {WindowsCredentialStore.MaxSecretChars}-character limit. Nothing was imported.");
                }

                if (!force && context.Store.Exists(name))
                {
                    throw new BackupException($"Secret '{name}' already exists. Nothing was imported. Use --force only if you want to replace existing secrets.");
                }
            }

            foreach (var (name, value) in secrets)
            {
                context.Store.Set(name, value);
                imported++;
            }
        }
        catch (SecretStoreException)
        {
            // Credential stores do not offer a transaction across items. Report partial progress
            // without reflecting an exception that might include a value from a future backend.
            throw new BackupException($"The credential store could not complete the import. {imported} secret(s) were imported before it stopped. Resolve storage access and retry (with --force if replacing those entries).");
        }
        finally
        {
            secrets.Clear();
        }

        context.Out.WriteLine($"Imported {imported} secret(s) into {context.Store.BackendName}. The encrypted backup was kept.");
        return ExitCodes.Success;
    }

    private static string ParsePath(string[] args, CliContext context, bool importing, out bool force)
    {
        string command = importing ? "import" : "export";
        force = false;
        string? path = null;
        foreach (string arg in args)
        {
            if (importing && arg == "--force")
            {
                force = true;
            }
            else if (!arg.StartsWith('-') && path is null)
            {
                path = arg;
            }
            else
            {
                // Never echo unexpected arguments: they might be a password.
                throw new UsageException($"Usage: agent-secrets {command} <file>" + (importing ? " [--force]" : "") + ". Passwords are accepted only at the hidden terminal prompt.");
            }
        }

        if (path is null)
        {
            throw new UsageException($"Usage: agent-secrets {command} <file>" + (importing ? " [--force]" : ""));
        }

        return Path.GetFullPath(path, context.WorkingDirectory);
    }

    private static void RequireTerminal(CliContext context, string command)
    {
        if (context.InputRedirected)
        {
            throw new BackupException($"'{command}' requires an interactive terminal. Run 'agent-secrets {command} <file>' yourself; passwords cannot be passed through arguments, environment variables or stdin.");
        }
    }

    private static string ReadPassword(CliContext context, string prompt)
    {
        context.Out.Write(prompt);
        string password = context.ReadSecretInteractive();
        context.Out.WriteLine();
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new BackupException("The backup password must not be empty or whitespace.");
        }

        return password; // Preserve whitespace and Unicode exactly; never trim a password.
    }

    private static void WriteNewFile(string path, byte[] archive)
    {
        // Only encrypted bytes reach disk. A sibling temporary file keeps the destination absent
        // until the entire archive is flushed, and Move refuses to replace an existing file.
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(temporary, options))
            {
                stream.Write(archive);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
