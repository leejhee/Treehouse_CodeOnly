using UnityEngine;
using StarterAssets;
using System.Collections;

public class CharacterControl : MonoBehaviour
{
    public static CharacterControl instance;
    public float walkingSpeed = 7.5f;
    public float runningSpeed = 11.5f;
    public float gravity = 20.0f;
    public Camera playerCamera;
    public float lookSpeed = 2.0f;
    public float lookXLimit = 45.0f;
    private bool _wasWalking = false;
    //public float startTime = 0f;

    public AudioSource audioWalk;
    public StarterAssetsInputs moveDetect;


    [HideInInspector]
    public Vector2 RunAxis;
    [HideInInspector]
    public Vector2 LookAxis;

    CharacterController characterController;
    Vector3 moveDirection = Vector3.zero;
    float rotationX = 0;
    

    [HideInInspector]
    public bool canMove = true;


    void Awake()
    {
        instance = this;
        characterController = GetComponent<CharacterController>();
        audioWalk= GetComponent<AudioSource>();
        moveDetect=GetComponent<StarterAssetsInputs>();
    }

    private void OnDestroy()
    {
        instance = null;
    }

    void Update()
    {
        //Debug.Log($"{moveDetect.move.x},{moveDetect.move.y}");
        Vector3 forward = transform.TransformDirection(Vector3.forward);
        Vector3 right = transform.TransformDirection(Vector3.right);
        bool isRunning = Input.GetKey(KeyCode.LeftShift);
        float curSpeedX = canMove ? (isRunning ? runningSpeed : walkingSpeed) * RunAxis.y : 0;
        float curSpeedY = canMove ? (isRunning ? runningSpeed : walkingSpeed) * RunAxis.x : 0;
        float movementDirectionY = moveDirection.y;
        moveDirection = (forward * curSpeedX) + (right * curSpeedY);
        moveDirection.y = movementDirectionY;

        if (!characterController.isGrounded)
        {
            moveDirection.y -= gravity * Time.deltaTime;
        }

        characterController.Move(moveDirection * Time.deltaTime);

        bool isWalking = moveDetect.move != Vector2.zero && WarpManager.instance.isField == true;

        if (isWalking != _wasWalking)
        {
            _wasWalking = isWalking;

            if (isWalking)
            {
                if (!audioWalk.isPlaying)
                    audioWalk.Play();
            }
            else
            {
                if (audioWalk.isPlaying)
                    audioWalk.Stop();
            }
        }
        // if (canMove)
        // {
        //     rotationX += -LookAxis.y * lookSpeed;
        //     rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);
        //     playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
        //     transform.rotation *= Quaternion.Euler(0, LookAxis.x * lookSpeed, 0);
        // }
        //
        // if (moveDetect.move != Vector2.zero && WarpManager.instance.isField == true)
        // {
        //     if (audioWalk.isPlaying == false)
        //     {
        //         //Debug.Log("재생할 수 있음");
        //         audioWalk.Play();
        //     }
        // }
        // else
        // {
        //     //Debug.Log("재생할 수 없음");
        //     if (audioWalk.isPlaying == true)
        //     {
        //         audioWalk.Stop();
        //     }            
        // }
    }
    /*public void PlaywalkOneShot(AudioClip clip, float volume)
    {
        audioWalk.PlayOneShot(clip, volume);
        startTime = Time.time;
    }
    public bool isPlaying()
    {
        if ((Time.time - startTime) >= audioWalk.clip.length)
        {
            Debug.Log("재생할 수 있음");
            return false;
        }
        Debug.Log("재생할 수 없음");
        return true;
    }*/
    

    /*public IEnumerator PlayWalk()
    {
        while (true)
        {
            if (isPlayingWalk == false && moveDetect.move != Vector2.zero
                && WarpManager.instance.isField == true)
            {
                isPlayingWalk = true;
                audioWalk.Play();
                yield return new WaitForSeconds(audioWalk.clip.length);
                isPlayingWalk = false;
                StartCoroutine(PlayWalk());
            }
            else
            {
                isPlayingWalk = false;
                audioWalk.Stop();
                yield return new WaitForSeconds(audioWalk.clip.length);
                StartCoroutine(PlayWalk());
            }
                
        }       
    }*/
}

