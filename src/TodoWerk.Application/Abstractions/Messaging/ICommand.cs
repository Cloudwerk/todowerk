namespace TodoWerk.Application.Abstractions.Messaging;

#pragma warning disable CA1040 // Marker interfaces are the dispatch contract for handlers; no members by design.
public interface ICommand;

public interface ICommand<TResponse>;
#pragma warning restore CA1040
