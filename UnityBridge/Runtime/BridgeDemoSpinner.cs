using UnityEngine;
namespace UnityLiveBridge
{
    public class BridgeDemoSpinner : MonoBehaviour
    {
        public float degreesPerSecond = 35;
        public Transform focus;
        public Color accent = new Color(0.2f,0.85f,1);
        public string[] notes = { "Edit my speed", "Change my array", "Assign a scene reference" };
        void Update() { transform.Rotate(Vector3.up,degreesPerSecond * Time.deltaTime,Space.World); }
    }
}
