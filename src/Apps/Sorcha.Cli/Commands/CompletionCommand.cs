// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using Sorcha.Cli.Infrastructure;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Shell completion script generator for bash, zsh, powershell, and fish.
/// </summary>
public class CompletionCommand : Command
{
    private readonly Argument<string> _shellArgument;

    public CompletionCommand()
        : base("completion", "Generate shell completion scripts\n\nExamples:\n  sorcha completion bash >> ~/.bashrc\n  sorcha completion zsh >> ~/.zshrc\n  sorcha completion powershell >> $PROFILE\n  sorcha completion fish > ~/.config/fish/completions/sorcha.fish")
    {
        _shellArgument = new Argument<string>("shell")
        {
            Description = "Shell type: bash, zsh, powershell, or fish"
        };
        _shellArgument.AcceptOnlyFromAmong("bash", "zsh", "powershell", "fish");

        Arguments.Add(_shellArgument);

        this.SetAction((ParseResult parseResult, CancellationToken ct) =>
        {
            var shell = parseResult.GetValue(_shellArgument)!;

            var script = shell switch
            {
                "bash" => GenerateBashCompletion(),
                "zsh" => GenerateZshCompletion(),
                "powershell" => GeneratePowerShellCompletion(),
                "fish" => GenerateFishCompletion(),
                _ => null
            };

            if (script == null)
            {
                ConsoleHelper.WriteError($"Unsupported shell: {shell}. Supported shells: bash, zsh, powershell, fish");
                return Task.FromResult(ExitCodes.ValidationError);
            }

            Console.Write(script);
            return Task.FromResult(ExitCodes.Success);
        });
    }

    /// <summary>
    /// Generates a bash completion script for the Sorcha CLI.
    /// </summary>
    private static string GenerateBashCompletion()
    {
        return """
               # Sorcha CLI bash completion
               # Add to ~/.bashrc or ~/.bash_profile:
               #   eval "$(sorcha completion bash)"

               _sorcha_completions()
               {
                   local cur prev commands subcommands
                   COMPREPLY=()
                   cur="${COMP_WORDS[COMP_CWORD]}"
                   prev="${COMP_WORDS[COMP_CWORD-1]}"

                   # Top-level commands
                   commands="auth bootstrap organization user service-principal register transaction wallet docket query peer blueprint participant credential validator admin schema operation action events invitation health verify platform config help version completion"

                   # Subcommands per top-level command
                   case "${COMP_WORDS[1]}" in
                       auth)
                           subcommands="login logout status refresh"
                           ;;
                       wallet)
                           subcommands="list get create recover delete sign access create-batch"
                           ;;
                       register)
                           subcommands="list get create delete update stats policy system export export-transactions"
                           ;;
                       blueprint)
                           subcommands="list get create publish delete versions instances export"
                           ;;
                       transaction)
                           subcommands="list get submit status"
                           ;;
                       docket)
                           subcommands="list get transactions"
                           ;;
                       organization)
                           subcommands="list get create update delete"
                           ;;
                       user)
                           subcommands="list get create update delete"
                           ;;
                       peer)
                           subcommands="list get status connect disconnect"
                           ;;
                       participant)
                           subcommands="list get create update delete search"
                           ;;
                       credential)
                           subcommands="list get create revoke verify"
                           ;;
                       validator)
                           subcommands="list get register approve reject metrics"
                           ;;
                       config)
                           subcommands="list get set profile"
                           ;;
                       *)
                           COMPREPLY=( $(compgen -W "${commands}" -- ${cur}) )
                           return 0
                           ;;
                   esac

                   if [ ${COMP_CWORD} -eq 2 ]; then
                       COMPREPLY=( $(compgen -W "${subcommands}" -- ${cur}) )
                       return 0
                   fi

                   # Common options
                   local options="--profile --output --quiet --verbose --help"

                   case "${prev}" in
                       --output|-o)
                           COMPREPLY=( $(compgen -W "table json csv yaml" -- ${cur}) )
                           return 0
                           ;;
                       --algorithm|-a)
                           COMPREPLY=( $(compgen -W "ED25519 NISTP256 RSA4096" -- ${cur}) )
                           return 0
                           ;;
                       --format|-f)
                           COMPREPLY=( $(compgen -W "json csv" -- ${cur}) )
                           return 0
                           ;;
                   esac

                   COMPREPLY=( $(compgen -W "${options}" -- ${cur}) )
                   return 0
               }

               complete -F _sorcha_completions sorcha
               """;
    }

    /// <summary>
    /// Generates a zsh completion script for the Sorcha CLI.
    /// </summary>
    private static string GenerateZshCompletion()
    {
        return """
               # Sorcha CLI zsh completion
               # Add to ~/.zshrc:
               #   eval "$(sorcha completion zsh)"

               _sorcha() {
                   local -a commands subcommands global_options
                   commands=(
                       'auth:Manage authentication'
                       'bootstrap:Bootstrap platform configuration'
                       'organization:Manage organizations'
                       'user:Manage users'
                       'service-principal:Manage service principals'
                       'register:Manage registers'
                       'transaction:Manage transactions'
                       'wallet:Manage wallets'
                       'docket:Manage dockets'
                       'query:Query register data'
                       'peer:Manage P2P peers'
                       'blueprint:Manage blueprints'
                       'participant:Manage participants'
                       'credential:Manage credentials'
                       'validator:Manage validators'
                       'admin:Admin operations'
                       'schema:Manage schemas'
                       'operation:Track operations'
                       'action:Manage actions'
                       'events:Real-time event streaming'
                       'invitation:Manage invitations'
                       'health:Health checks'
                       'verify:Verification operations'
                       'platform:Platform management'
                       'config:Configuration management'
                       'help:Getting started guide'
                       'version:Display version'
                       'completion:Generate shell completions'
                   )

                   global_options=(
                       '--profile[Configuration profile]:profile name:'
                       '--output[Output format]:format:(table json csv yaml)'
                       '--quiet[Suppress non-essential output]'
                       '--verbose[Enable verbose logging]'
                       '--help[Show help]'
                   )

                   _arguments -C \
                       '1:command:->command' \
                       '2:subcommand:->subcommand' \
                       '*::options:->options'

                   case $state in
                       command)
                           _describe 'command' commands
                           ;;
                       subcommand)
                           case $words[1] in
                               wallet)
                                   subcommands=('list' 'get' 'create' 'recover' 'delete' 'sign' 'access' 'create-batch')
                                   _describe 'subcommand' subcommands
                                   ;;
                               register)
                                   subcommands=('list' 'get' 'create' 'delete' 'update' 'stats' 'policy' 'system' 'export' 'export-transactions')
                                   _describe 'subcommand' subcommands
                                   ;;
                               blueprint)
                                   subcommands=('list' 'get' 'create' 'publish' 'delete' 'versions' 'instances' 'export')
                                   _describe 'subcommand' subcommands
                                   ;;
                               auth)
                                   subcommands=('login' 'logout' 'status' 'refresh')
                                   _describe 'subcommand' subcommands
                                   ;;
                               *)
                                   _describe 'options' global_options
                                   ;;
                           esac
                           ;;
                       options)
                           _describe 'options' global_options
                           ;;
                   esac
               }

               compdef _sorcha sorcha
               """;
    }

    /// <summary>
    /// Generates a PowerShell completion script for the Sorcha CLI.
    /// </summary>
    private static string GeneratePowerShellCompletion()
    {
        return """
               # Sorcha CLI PowerShell completion
               # Add to your $PROFILE:
               #   sorcha completion powershell | Out-String | Invoke-Expression

               Register-ArgumentCompleter -Native -CommandName sorcha -ScriptBlock {
                   param($wordToComplete, $commandAst, $cursorPosition)

                   $commands = @{
                       '' = @('auth', 'bootstrap', 'organization', 'user', 'service-principal', 'register', 'transaction', 'wallet', 'docket', 'query', 'peer', 'blueprint', 'participant', 'credential', 'validator', 'admin', 'schema', 'operation', 'action', 'events', 'invitation', 'health', 'verify', 'platform', 'config', 'help', 'version', 'completion')
                       'wallet' = @('list', 'get', 'create', 'recover', 'delete', 'sign', 'access', 'create-batch')
                       'register' = @('list', 'get', 'create', 'delete', 'update', 'stats', 'policy', 'system', 'export', 'export-transactions')
                       'blueprint' = @('list', 'get', 'create', 'publish', 'delete', 'versions', 'instances', 'export')
                       'auth' = @('login', 'logout', 'status', 'refresh')
                       'transaction' = @('list', 'get', 'submit', 'status')
                       'docket' = @('list', 'get', 'transactions')
                       'organization' = @('list', 'get', 'create', 'update', 'delete')
                       'user' = @('list', 'get', 'create', 'update', 'delete')
                       'peer' = @('list', 'get', 'status', 'connect', 'disconnect')
                       'participant' = @('list', 'get', 'create', 'update', 'delete', 'search')
                       'credential' = @('list', 'get', 'create', 'revoke', 'verify')
                       'validator' = @('list', 'get', 'register', 'approve', 'reject', 'metrics')
                       'config' = @('list', 'get', 'set', 'profile')
                   }

                   $elements = $commandAst.CommandElements
                   $command = ''

                   if ($elements.Count -ge 2) {
                       $command = $elements[1].ToString()
                   }

                   $completions = if ($elements.Count -le 2) {
                       $commands['']
                   } elseif ($commands.ContainsKey($command)) {
                       $commands[$command]
                   } else {
                       @()
                   }

                   $completions | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
                       [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                   }
               }
               """;
    }

    /// <summary>
    /// Generates a fish completion script for the Sorcha CLI.
    /// </summary>
    private static string GenerateFishCompletion()
    {
        return """
               # Sorcha CLI fish completion
               # Save to ~/.config/fish/completions/sorcha.fish

               # Disable file completions by default
               complete -c sorcha -f

               # Top-level commands
               complete -c sorcha -n '__fish_use_subcommand' -a 'auth' -d 'Manage authentication'
               complete -c sorcha -n '__fish_use_subcommand' -a 'bootstrap' -d 'Bootstrap platform configuration'
               complete -c sorcha -n '__fish_use_subcommand' -a 'organization' -d 'Manage organizations'
               complete -c sorcha -n '__fish_use_subcommand' -a 'user' -d 'Manage users'
               complete -c sorcha -n '__fish_use_subcommand' -a 'service-principal' -d 'Manage service principals'
               complete -c sorcha -n '__fish_use_subcommand' -a 'register' -d 'Manage registers'
               complete -c sorcha -n '__fish_use_subcommand' -a 'transaction' -d 'Manage transactions'
               complete -c sorcha -n '__fish_use_subcommand' -a 'wallet' -d 'Manage wallets'
               complete -c sorcha -n '__fish_use_subcommand' -a 'docket' -d 'Manage dockets'
               complete -c sorcha -n '__fish_use_subcommand' -a 'query' -d 'Query register data'
               complete -c sorcha -n '__fish_use_subcommand' -a 'peer' -d 'Manage P2P peers'
               complete -c sorcha -n '__fish_use_subcommand' -a 'blueprint' -d 'Manage blueprints'
               complete -c sorcha -n '__fish_use_subcommand' -a 'participant' -d 'Manage participants'
               complete -c sorcha -n '__fish_use_subcommand' -a 'credential' -d 'Manage credentials'
               complete -c sorcha -n '__fish_use_subcommand' -a 'validator' -d 'Manage validators'
               complete -c sorcha -n '__fish_use_subcommand' -a 'admin' -d 'Admin operations'
               complete -c sorcha -n '__fish_use_subcommand' -a 'schema' -d 'Manage schemas'
               complete -c sorcha -n '__fish_use_subcommand' -a 'operation' -d 'Track operations'
               complete -c sorcha -n '__fish_use_subcommand' -a 'action' -d 'Manage actions'
               complete -c sorcha -n '__fish_use_subcommand' -a 'events' -d 'Real-time event streaming'
               complete -c sorcha -n '__fish_use_subcommand' -a 'invitation' -d 'Manage invitations'
               complete -c sorcha -n '__fish_use_subcommand' -a 'health' -d 'Health checks'
               complete -c sorcha -n '__fish_use_subcommand' -a 'verify' -d 'Verification operations'
               complete -c sorcha -n '__fish_use_subcommand' -a 'platform' -d 'Platform management'
               complete -c sorcha -n '__fish_use_subcommand' -a 'config' -d 'Configuration management'
               complete -c sorcha -n '__fish_use_subcommand' -a 'help' -d 'Getting started guide'
               complete -c sorcha -n '__fish_use_subcommand' -a 'version' -d 'Display version'
               complete -c sorcha -n '__fish_use_subcommand' -a 'completion' -d 'Generate shell completions'

               # wallet subcommands
               complete -c sorcha -n '__fish_seen_subcommand_from wallet' -a 'list' -d 'List all wallets'
               complete -c sorcha -n '__fish_seen_subcommand_from wallet' -a 'get' -d 'Get a wallet'
               complete -c sorcha -n '__fish_seen_subcommand_from wallet' -a 'create' -d 'Create a wallet'
               complete -c sorcha -n '__fish_seen_subcommand_from wallet' -a 'recover' -d 'Recover a wallet'
               complete -c sorcha -n '__fish_seen_subcommand_from wallet' -a 'delete' -d 'Delete a wallet'
               complete -c sorcha -n '__fish_seen_subcommand_from wallet' -a 'sign' -d 'Sign data'
               complete -c sorcha -n '__fish_seen_subcommand_from wallet' -a 'access' -d 'Manage access'
               complete -c sorcha -n '__fish_seen_subcommand_from wallet' -a 'create-batch' -d 'Create wallets in batch'

               # register subcommands
               complete -c sorcha -n '__fish_seen_subcommand_from register' -a 'list' -d 'List registers'
               complete -c sorcha -n '__fish_seen_subcommand_from register' -a 'get' -d 'Get a register'
               complete -c sorcha -n '__fish_seen_subcommand_from register' -a 'create' -d 'Create a register'
               complete -c sorcha -n '__fish_seen_subcommand_from register' -a 'delete' -d 'Delete a register'
               complete -c sorcha -n '__fish_seen_subcommand_from register' -a 'update' -d 'Update a register'
               complete -c sorcha -n '__fish_seen_subcommand_from register' -a 'stats' -d 'Register statistics'
               complete -c sorcha -n '__fish_seen_subcommand_from register' -a 'policy' -d 'Manage policy'
               complete -c sorcha -n '__fish_seen_subcommand_from register' -a 'system' -d 'System register'
               complete -c sorcha -n '__fish_seen_subcommand_from register' -a 'export' -d 'Export register as JSON'
               complete -c sorcha -n '__fish_seen_subcommand_from register' -a 'export-transactions' -d 'Export transactions'

               # blueprint subcommands
               complete -c sorcha -n '__fish_seen_subcommand_from blueprint' -a 'list' -d 'List blueprints'
               complete -c sorcha -n '__fish_seen_subcommand_from blueprint' -a 'get' -d 'Get a blueprint'
               complete -c sorcha -n '__fish_seen_subcommand_from blueprint' -a 'create' -d 'Create a blueprint'
               complete -c sorcha -n '__fish_seen_subcommand_from blueprint' -a 'publish' -d 'Publish a blueprint'
               complete -c sorcha -n '__fish_seen_subcommand_from blueprint' -a 'delete' -d 'Delete a blueprint'
               complete -c sorcha -n '__fish_seen_subcommand_from blueprint' -a 'versions' -d 'List versions'
               complete -c sorcha -n '__fish_seen_subcommand_from blueprint' -a 'instances' -d 'List instances'
               complete -c sorcha -n '__fish_seen_subcommand_from blueprint' -a 'export' -d 'Export blueprint as JSON'

               # auth subcommands
               complete -c sorcha -n '__fish_seen_subcommand_from auth' -a 'login' -d 'Log in'
               complete -c sorcha -n '__fish_seen_subcommand_from auth' -a 'logout' -d 'Log out'
               complete -c sorcha -n '__fish_seen_subcommand_from auth' -a 'status' -d 'Auth status'
               complete -c sorcha -n '__fish_seen_subcommand_from auth' -a 'refresh' -d 'Refresh token'

               # completion subcommands
               complete -c sorcha -n '__fish_seen_subcommand_from completion' -a 'bash' -d 'Bash completions'
               complete -c sorcha -n '__fish_seen_subcommand_from completion' -a 'zsh' -d 'Zsh completions'
               complete -c sorcha -n '__fish_seen_subcommand_from completion' -a 'powershell' -d 'PowerShell completions'
               complete -c sorcha -n '__fish_seen_subcommand_from completion' -a 'fish' -d 'Fish completions'

               # Global options
               complete -c sorcha -l profile -s p -d 'Configuration profile'
               complete -c sorcha -l output -s o -d 'Output format' -a 'table json csv yaml'
               complete -c sorcha -l quiet -s q -d 'Suppress non-essential output'
               complete -c sorcha -l verbose -s v -d 'Enable verbose logging'
               complete -c sorcha -l help -s h -d 'Show help'
               """;
    }
}
