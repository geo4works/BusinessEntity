namespace BusinessEntity.Infrastructure.Modularity;

// Владелец подключает scoped runtime до первого render маршрутизируемых страниц.
public interface IWindowRuntimeInitializer
{
    // Синхронно устанавливает обработчики своего circuit, не читая данные чужих владельцев.
    void Start();
}

// Единственный стартовый барьер окна не зависит от времени создания компонентов shell.
public sealed class WindowRuntimeStartup(IEnumerable<IWindowRuntimeInitializer> initializers)
{
    private bool Started;
    // Сначала готовы подписчики, затем App строит Router и прикладные компоненты.
    public void Start()
    {
        if (Started) return;
        foreach (var initializer in initializers) initializer.Start();
        Started = true;
    }
}
