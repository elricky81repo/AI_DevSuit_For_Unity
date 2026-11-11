using UnityEngine;
using UnityEngine.Events;

public class EventoHola : MonoBehaviour
{
    [System.Serializable]
    public class HolaEvent : UnityEvent<string> { }

    public HolaEvent OnHola;

    [ContextMenu("Disparar Hola")]
    public void DispararHola()
    {
        if (OnHola == null)
            OnHola = new HolaEvent();

        OnHola.Invoke("hola");
    }

    private void Start()
    {
        DispararHola();
    }
}