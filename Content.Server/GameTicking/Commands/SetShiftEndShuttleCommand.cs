using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.GameTicking.Commands
{
    [AdminCommand(AdminFlags.Round)]
    sealed class SetShiftEndShuttleCommand : IConsoleCommand
    {
        [Dependency] private readonly IEntityManager _e = default!;

        public string Command => "setshiftendshuttle";
        public string Description => "Sets whether the emergency shuttle should automatically be called when 30 minutes remain in the shift.";
        public string Help => "setshiftendshuttle <true|false> - Enables or disables automatic shuttle calling based on shift end time. Defaults to true.";

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var ticker = _e.System<GameTicker>();

            if (ticker.RunLevel != GameRunLevel.InRound)
            {
                shell.WriteLine("This can only be executed while the game is in a round.");
                return;
            }

            if (args.Length < 1)
            {
                // Show current state
                shell.WriteLine($"Shift end auto-call is currently {(ticker.ShiftEndAutoCallEnabled ? "enabled" : "disabled")}.");
                shell.WriteLine(Help);
                return;
            }

            if (!bool.TryParse(args[0], out var enabled))
            {
                shell.WriteError("Invalid boolean value. Use 'true' or 'false'.");
                return;
            }

            ticker.ShiftEndAutoCallEnabled = enabled;
            shell.WriteLine($"Shift end auto-call {(enabled ? "enabled" : "disabled")}.");
        }
    }
}
