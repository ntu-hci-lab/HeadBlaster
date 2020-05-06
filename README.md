### Nozzle clip mount for the HTC Vive Pro headset

3D printing models for the front/back clip mounts: https://www.tinkercad.com/things/bcwnY3KMnXE-minidesign

### Arduino

- HeadBlaster_AirPressure_Controller.ino 
  - Receives signal from Unity to control ITV2050 (air pressure regulator) and SYJ712 (solenoid valve)

### Unity

- ACListener.cs
  - Listens to the event / data from commercial game Assetto Corsa through UDP.
- ACPacketStruct.cs
  - Layouts the data received from Assetto Corsa as specified in official documentation: https://docs.google.com/document/d/1KfkZiIluXZ6mMhLWfDX1qAGbvhGRC3ZUzjVIt5FQpp4/pub
- AirDriVRSystem.cs  **(Unity to Arduino Interface)**
  - Core script that drives the hardware system. Singleton instance can be initialized by static methods.
  - Sends force data (Byte) to Arduino by SerialPort
- BackgroundVRListener.cs
  - Listens to VR headset's position and rotation when using external VR applications.
  - **Make sure XR support is turned off when using this script as it may clash with the underlying VR instance and result in a crash.**
- GForceMonitor.cs
  - Calculates G force in the VR game based on either position delta or rigidbody data and drives AirDriVRSystem automatically.
- SingletonMonoBehaviour.cs
  - A singleton helper class; subclassed by AirDriVRSystem.
- SurfController.cs  **(scripts only, without scenes)**
  - Demonstrates how to manually override force using the provided APIs. (BoostOverrideCoroutine)
