using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class followCamera : MonoBehaviour
{

    public Camera playerCamera;
    public Text playerPosition;
    // Start is called before the first frame update
    void Start()
    {
       

    }

    // Update is called once per frame
    void Update()
    {

        this.transform.position = Vector3.MoveTowards(this.transform.position, playerCamera.transform.position, 1f);
        this.transform.rotation = playerCamera.transform.rotation;
       // playerPosition.text = "x: " + this.transform.position.x + " | y: " + this.transform.position.y + " | z: " + this.transform.position.z;
    }
}
