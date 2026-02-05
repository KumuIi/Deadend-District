using UnityEngine;

public class WeaponController : MonoBehaviour
{
    public GameObject bulletHolePrefab;
    public Transform gunMuzzle;
    private Camera cam;
    public float range = 100f;

    void Start()
    {
        cam = Camera.main;
    }
    void Update()
    {
        shoot();
    }
    private void shoot()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            Vector3 targetPoint;
            RaycastHit hit;
            
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, range))
            {
                targetPoint = hit.point;
                Debug.DrawRay(cam.transform.position, cam.transform.forward * hit.distance, Color.orange, 2.0f);
            }
            else
            {
                targetPoint = cam.transform.position + cam.transform.forward * range;
                Debug.DrawRay(cam.transform.position, cam.transform.forward * range, Color.red, 2.0f);
            }
            
            Debug.DrawLine(gunMuzzle.position, targetPoint, Color.green, 2.0f);
            
            if (Physics.Linecast(gunMuzzle.position, targetPoint, out hit))
            {
                Instantiate(bulletHolePrefab, hit.point + hit.normal * 0.01f, Quaternion.LookRotation(hit.normal));
            }
        }
    }
}
