namespace PMDA.Api.Contracts;

public sealed record DeleteFlightsRequest(Guid[] Ids);

public sealed record DeleteResultDto(int Deleted);
