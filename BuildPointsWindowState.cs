namespace BuildPoints
{
    /// <summary>
    /// Position and open/closed state of one toolbar window. The scenario
    /// keeps one of these per editor (VAB, SPH) and saves them with the game.
    /// </summary>
    public class BuildPointsWindowState
    {
        public float x;
        public float y;
        public bool open;

        public BuildPointsWindowState(float defaultX, float defaultY)
        {
            x = defaultX;
            y = defaultY;
        }

        public void Load(ConfigNode node)
        {
            node.TryGetValue("x", ref x);
            node.TryGetValue("y", ref y);
            node.TryGetValue("open", ref open);
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("x", x);
            node.AddValue("y", y);
            node.AddValue("open", open);
        }
    }
}
