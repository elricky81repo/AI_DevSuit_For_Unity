using UnityEngine;

public class RotateWithBButton : MonoBehaviour
{
    [SerializeField] private string buttonName = "Fire2";
    [SerializeField] private Vector3 rotationAxis = Vector3.up;
    [SerializeField] private float rotationSpeed = 90f;

    void Update()
    {
        if (Input.GetButton(buttonName))
        {
            transform.Rotate(rotationAxis.normalized * rotationSpeed * Time.deltaTime, Space.Self);
        }
    }
}