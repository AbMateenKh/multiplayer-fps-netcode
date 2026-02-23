using UnityEngine;
using UnityEngine.UI;

public class TestingNetcodeUI : MonoBehaviour
{

    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;

    private void Start()
    {
        

        hostButton.onClick.AddListener(() =>
        {
            Debug.Log("Starting Host");
            Unity.Netcode.NetworkManager.Singleton.StartHost();
            Hide();
        });
        clientButton.onClick.AddListener(() =>
        {
            Debug.Log("Starting Client");
            Unity.Netcode.NetworkManager.Singleton.StartClient();
            Hide();
        });

    }


    private void Update()
    {
        // Force cursor to stay unlocked and visible while this UI is active
        if (gameObject.activeSelf)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

       

    }

    private void Show()
    {
        gameObject.SetActive(true);
    }



    private void Hide()
    {
        gameObject.SetActive(false);

        // Now lock cursor for gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

}
