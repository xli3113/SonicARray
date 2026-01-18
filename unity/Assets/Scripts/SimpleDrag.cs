using UnityEngine;

/// <summary>
/// Simple drag script for testing in Unity Editor
/// Replace with Meta XR SDK grab interaction in production
/// </summary>
public class SimpleDrag : MonoBehaviour {
    private bool isDragging = false;
    private Camera mainCamera;
    private Vector3 offset;
    
    void Start() {
        mainCamera = Camera.main;
        if (mainCamera == null) {
            mainCamera = FindObjectOfType<Camera>();
        }
    }
    
    void OnMouseDown() {
        if (mainCamera != null) {
            isDragging = true;
            Vector3 mousePos = Input.mousePosition;
            mousePos.z = Vector3.Distance(mainCamera.transform.position, transform.position);
            Vector3 worldPos = mainCamera.ScreenToWorldPoint(mousePos);
            offset = transform.position - worldPos;
        }
    }
    
    void OnMouseDrag() {
        if (isDragging && mainCamera != null) {
            Vector3 mousePos = Input.mousePosition;
            mousePos.z = Vector3.Distance(mainCamera.transform.position, transform.position);
            Vector3 worldPos = mainCamera.ScreenToWorldPoint(mousePos);
            transform.position = worldPos + offset;
        }
    }
    
    void OnMouseUp() {
        isDragging = false;
    }
}
