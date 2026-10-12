using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameRadar.App.Resources;
using GameRadar.Core.Logging;

namespace GameRadar.App.ViewModels;

/// <summary>
/// The error banner, visible from any page, and the error log: an error goes
/// to the banner and to the log with its original exception (its stack trace
/// is what explains a bug met by a friend).
/// </summary>
public sealed partial class NoticeCenter(ErrorLog log) : ObservableObject
{
    /// <summary>Last error worth the user's attention, shown in a banner until dismissed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string Message { get; private set; } = string.Empty;

    public bool HasMessage => Message.Length > 0;

    [RelayCommand]
    public void Dismiss() => Message = string.Empty;

    /// <summary>Shows <paramref name="message"/> in the banner only: nothing worth the log (a click the app refuses, a save that can be retried).</summary>
    public void Show(string message) => Message = message;

    /// <summary>Shows <paramref name="message"/> in the banner and writes it to the log.</summary>
    public void Report(string context, string message, Exception? ex = null)
    {
        Message = message;
        log.Write(context, message, ex);
    }

    /// <summary>
    /// An error no code path expected (see the global handlers in App): it
    /// is written to the log and the user is told where to find it.
    /// </summary>
    public void ReportUnexpected(string context, Exception ex)
    {
        log.Write(context, "Unexpected error", ex);
        Message = Strings.Error_UnexpectedLogged;
    }

    /// <summary>
    /// The message shown for an error of an online service, from the most
    /// specific to the most generic: the connector's own (already translated),
    /// then no network, too slow, an unexpected answer (only where the call
    /// reads one: without <paramref name="badAnswer"/>, it is unexpected).
    /// </summary>
    /// <param name="unexpected">The sentence of an unexpected error ({0} = its message), when more precise than the general one.</param>
    public static string ServiceErrorMessage<TServiceException>(
        Exception ex, string unreachable, string slow, string? badAnswer = null, string? unexpected = null)
        where TServiceException : Exception => ex switch
    {
        TServiceException => ex.Message,
        HttpRequestException => unreachable,
        TaskCanceledException => slow,
        JsonException when badAnswer is not null => badAnswer,
        _ => Localization.Format(unexpected ?? Strings.Error_Unexpected, ex.Message)
    };
}
