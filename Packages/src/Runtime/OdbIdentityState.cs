namespace oojjrs.odb
{
    public sealed class OdbIdentityState
    {
        public long HighWaterMark { get; }
        public string KeyTypeName { get; }
        public string Name { get; }
        public string Scope { get; }

        internal string StateKey => Scope + "\0" + Name;

        internal OdbIdentityState(string scope, string name, string keyTypeName, long highWaterMark)
        {
            Scope = scope;
            Name = name;
            KeyTypeName = keyTypeName;
            HighWaterMark = highWaterMark;
        }
    }
}
