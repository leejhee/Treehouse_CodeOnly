using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Character_Controller: MonoBehaviour
{
    public static GameObject characterObj;
    // Start is called before the first frame update
    public Transform character;//플레이어 객체를 만들면 사용할 예정
    public Rigidbody player;
    void Start()
    {
        characterObj = this.gameObject;
    }

    private void OnDestroy()
    {
        characterObj = null;
    }


    private void FixedUpdate()
    {
        Character_Move();
    }

    void Character_Move()
    {
        Vector2 moveInput = JoyPadHandler.velocity; 
        bool isMove = moveInput.magnitude > 0; 
        
        if (isMove) 
        { 
            //상훈 : 1127 비활            
            Vector3 lookForward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized; 
            Vector3 lookRight = new Vector3(transform.right.x, 0f, transform.right.z).normalized; 
            Vector3 moveDir = lookForward * moveInput.y + lookRight * moveInput.x; 
            character.forward = moveDir; 
            transform.position += moveDir * Time.deltaTime; 
        }        

    }


    void OnTriggerEnter(Collider other)
    {

    }

    void OnCollisionEnter(Collision ground)
    {
        
    }
   
}
