using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Parse.Core.Internal
{
    public class MutableObjectState : IObjectState
    {
        public bool IsNew { get; set; }

        public string ClassName { get; set; }

        public string ObjectId { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public DateTime? CreatedAt { get; set; }

        public IDictionary<string, object> ServerData { get; set; } = new Dictionary<string, object> { };

        public object this[string key] => ServerData[key];

        public bool ContainsKey(string key) => ServerData.ContainsKey(key);

        public void Apply(IDictionary<string, IParseFieldOperation> operationSet)
        {
            foreach (KeyValuePair<string, IParseFieldOperation> pair in operationSet)
            {
                ServerData.TryGetValue(pair.Key, out object oldValue);

                if (pair.Value.Apply(oldValue, pair.Key) is object newValue && newValue != ParseDeleteOperation.DeleteToken)
                    ServerData[pair.Key] = newValue;
                else
                    ServerData.Remove(pair.Key);
            }
        }

        public void Apply(IObjectState other)
        {
            IsNew = other.IsNew;

            if (other.ObjectId != null)
                ObjectId = other.ObjectId;
            if (other.UpdatedAt != null)
                UpdatedAt = other.UpdatedAt;
            if (other.CreatedAt != null)
                CreatedAt = other.CreatedAt;

            foreach (KeyValuePair<string, object> pair in other)
                ServerData[pair.Key] = pair.Value;
        }

        public IObjectState MutatedClone(Action<MutableObjectState> func)
        {
            MutableObjectState clone = MutableClone();
            func(clone);
            return clone;
        }

        protected virtual MutableObjectState MutableClone() => new MutableObjectState
        {
            IsNew = IsNew,
            ClassName = ClassName,
            ObjectId = ObjectId,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            ServerData = this.ToDictionary(t => t.Key, t => t.Value)
        };

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator() => ServerData.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<KeyValuePair<string, object>>) this).GetEnumerator();
    }
}
