namespace BusinessEntity.Infrastructure.Api;

// Только metadata зарегистрированного owner runtime; HTTP клиент не выбирает Kind/CLR method.
public sealed record ApiJobDefinition(string Owner,string Kind,string CapabilityId)
{public string Key=>Owner+":"+Kind;}
