using UnityEngine;

[AddComponentMenu("Custom/Say 'el gato esta en la caja'")]
public class SayElGatoEstaEnLaCaja : MonoBehaviour
{
    [TextArea]
    public string mensaje = "el gato esta en la caja";
    public bool decirEnStart = true;

    private void Start()
    {
        if (decirEnStart)
        {
            Decir();
        }
    }

    [ContextMenu("Decir ahora")]
    public void Decir()
    {
        Debug.Log(mensaje);
    }
}