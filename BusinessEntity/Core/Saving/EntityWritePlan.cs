namespace BusinessEntity.Core.Saving;

// Общий движок готовит граф/payload описателем, владелец добавляет свою авторизацию и один Save.
public sealed record EntityWritePlan(Classes.BusinessEntity Entity,EntitySaveBatch Batch);
