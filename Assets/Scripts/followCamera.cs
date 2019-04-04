using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class followCamera : MonoBehaviour
{

    public Camera playerCamera;
    // Start is called before the first frame update
    void Start()
    {
        

    }

    // Update is called once per frame
    void Update()
    {

        this.transform.position = Vector3.MoveTowards(this.transform.position, playerCamera.transform.position, 1f);
        this.transform.rotation = playerCamera.transform.rotation;

    }
}
