using System.Net;
using OpenShift;

sealed partial class MockOpenShiftServer
{
    interface IResourceController
    {
        object Value { get; }
    }

    interface IResourceController<T> : IResourceController
    {
        new T Value { get; }
        T Get();
        T Patch(T patch);
        T Replace(T value);
    }

    sealed class ResourceController<T> : IResourceController<T> where T : class
    {
        public delegate T OnGetDelegate(int counter, T value, Func<Action<T>, T> update);

        private readonly object _gate = new();
        private readonly Action<T>? _onSet;
        private readonly OnGetDelegate _onGet;
        private T _value;
        private int _getCounter;

        public ResourceController(T value, Action<T>? onSet = null, OnGetDelegate? onGet = null)
        {
            _onGet ??= (i, value, update) => value;
            _onSet = onSet;

            value = Resource.Clone(value);
            onSet?.Invoke(value);
            _value = value;
        }

        public T Value => Resource.Clone(_value);

        object IResourceController.Value => this.Value;

        public T Get()
        {
            lock (_gate)
            {
                _getCounter++;
                return _onGet(_getCounter, Value, Update);
            }
        }

        public T Patch(T patch)
        {
            patch = Resource.Clone(patch);
            return Update(current => Resource.StrategicMergeObjectWith(current, patch));
        }

        public T Replace(T value)
        {
            value = Resource.Clone(value);
            if (!TryReplace(value))
            {
                throw new OpenShiftClientException("Version mismatch", HttpStatusCode.Conflict);
            }
            return Resource.Clone(value);
        }

        private bool TryReplace(T newValue, bool emitSet = true)
        {
            lock (_gate)
            {
                T currentValue = _value;
                if (Resource.GetResourceVersion(currentValue) != Resource.GetResourceVersion(newValue))
                {
                    return false;
                }
                string currentResourceVersion = Resource.GetResourceVersion(currentValue) ?? "0";
                Resource.SetResourceVersion(newValue, (int.Parse(currentResourceVersion) + 1).ToString());
                if (emitSet)
                {
                    _onSet?.Invoke(newValue);
                }
                _value = newValue;
                return true;
            }
        }

        private T Update(Action<T> update)
        {
            do
            {
                T value = Resource.Clone(_value);
                update(value);
                if (TryReplace(value, emitSet: false))
                {
                    return Resource.Clone(value);
                }
            } while (true);
        }
    }
}