using UnityEngine;

[DisallowMultipleComponent]
public class AllDirectionRotator : MonoBehaviour
{
    [SerializeField]
    private Vector3 rotationSpeed = new Vector3(30f, 45f, 60f);

    private void Update()
    {
        transform.Rotate(rotationSpeed * Time.deltaTime, Space.Self);
    }
}