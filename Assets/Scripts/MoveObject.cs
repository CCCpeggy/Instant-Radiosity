using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveObject : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        float scale = 2f * Time.deltaTime;
        Vector3 pos = transform.position;
        Quaternion rotation = transform.rotation;
        if (Input.GetKey("left")) pos.x += 0.05f * scale;
        if (Input.GetKey("right")) pos.x -= 0.05f * scale;
        if (Input.GetKey("up")) pos.z -= 0.05f * scale;
        if (Input.GetKey("down")) pos.z += 0.05f * scale;
        if (Input.GetKey("down")) Debug.Log("ssssss");


        if (Input.GetKey("a")) pos.x += 0.05f * scale;
        if (Input.GetKey("d")) pos.x -= 0.05f * scale;
        if (Input.GetKey("w")) pos.z -= 0.05f * scale;
        if (Input.GetKey("s")) pos.z += 0.05f * scale;

        
        if (Input.GetKey("q")) rotation *= Quaternion.Euler(0, -5 * scale, 0);
        if (Input.GetKey("e")) rotation *= Quaternion.Euler(0, 5 * scale, 0);
        transform.position = pos;
        transform.rotation = rotation;
    }
}
