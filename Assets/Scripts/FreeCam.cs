using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
public class FreeCam : MonoBehaviour
{
    public bool canMove = true;
    public Vector3 mousePosition = Vector3.zero;
    public float rSpeed = 1;
    public float tSpeed = 0.1f;
    public EventSystem es;
    public InputField panSpeed;
    public InputField movSpeed;
    public float zoomSpeed = 10.0f;
    public Vector3 minPosition = new Vector3(-5f, 0f, -10f);
    public Vector3 maxPosition = new Vector3(5f, 10f, 10f);
    public Transform target;
    public Vector3 initialPosition;
    public Quaternion initialRotation;

    void Start()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        es = EventSystem.current; // EventSystemへの参照を取得
        mousePosition = Input.mousePosition;
        panSpeed.text = (rSpeed * 100).ToString();
        movSpeed.text = (tSpeed * 100).ToString();
        GameObject rootObject = GameObject.Find("Target");
        if (rootObject != null)
        {
            target = rootObject.transform;
            transform.LookAt(target);
        }
        
    }

    void FixedUpdate()
    {


        Vector3 diff = mousePosition - Input.mousePosition;
        canMove = !es.IsPointerOverGameObject();
        if (canMove)
        {
            if (Input.GetMouseButton(1) && !es.IsPointerOverGameObject())
            {
                transform.RotateAround(target.position, Vector3.up, -diff.x * rSpeed);
                transform.RotateAround(target.position, transform.right, diff.y * rSpeed);
            }

            if (Input.GetMouseButton(2))
            {
                Vector3 proposedTranslation = new Vector3(diff.x * tSpeed, diff.y * tSpeed, 0);
                Vector3 newPosition = transform.position + transform.TransformDirection(proposedTranslation);
                newPosition.x = Mathf.Clamp(newPosition.x, minPosition.x, maxPosition.x);
                newPosition.y = Mathf.Clamp(newPosition.y, minPosition.y, maxPosition.y);
                newPosition.z = Mathf.Clamp(newPosition.z, minPosition.z, maxPosition.z);
                transform.position = newPosition;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0.0f)
            {
                Vector3 proposedZoom = transform.forward * scroll * zoomSpeed;
                Vector3 newPosition = transform.position + proposedZoom;
                newPosition.z = Mathf.Clamp(newPosition.z, minPosition.z, maxPosition.z);
                transform.position = newPosition;
            }
        }
        mousePosition = Input.mousePosition;
    }

    public void updateSpeeds()
    {
        float speed;
        if (float.TryParse(panSpeed.text, out speed))
            rSpeed = speed / 100;

        if (float.TryParse(movSpeed.text, out speed))
            tSpeed = speed / 100;
    }

    public void ResetCameraPosition()
    {
        transform.position = initialPosition;
        transform.rotation = initialRotation;
    }
}
