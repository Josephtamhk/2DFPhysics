using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TF.Core;
using FixedPointy;

public class PlayerController : MonoBehaviour
{
    public TFRigidbody rb;
    public CharacterController2D charController;

    public Fix movementSpeed = (Fix)0.3f;

    public FixVec2 velo;

    private void Update()
    {
        Fix dt = (Fix)Time.deltaTime;
        if (Input.GetKey(KeyCode.A))
        {
            velo.x = -movementSpeed * dt;
        } else if (Input.GetKey(KeyCode.D))
        {
            velo.x = movementSpeed * dt;
        }
        else
        {
            velo.x = 0;
        }

        if (Input.GetKey(KeyCode.W))
        {
            velo.y = movementSpeed * dt;
        }
        else if (Input.GetKey(KeyCode.S))
        {
            velo.y = -movementSpeed * dt;
        }
        else
        {
            velo.y = 0;
        }

        charController.move(velo);
    }
}
