using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AvanceConI : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] private float velocidad = 5f;
    [SerializeField] private bool usarFisicas = true;

    private Rigidbody rb;
    private Vector3 inputDireccion;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (usarFisicas)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }
    }

    private void Update()
    {
        inputDireccion = Vector3.zero;

        if (Input.GetKey(KeyCode.I))
        {
            inputDireccion += transform.forward;
        }
    }

    private void FixedUpdate()
    {
        if (usarFisicas)
        {
            Vector3 velocidadObjetivo = inputDireccion.normalized * velocidad;
            Vector3 nuevaVelocidad = new Vector3(velocidadObjetivo.x, rb.velocity.y, velocidadObjetivo.z);
            rb.velocity = nuevaVelocidad;
        }
        else
        {
            transform.position += inputDireccion.normalized * velocidad * Time.fixedDeltaTime;
        }
    }
}