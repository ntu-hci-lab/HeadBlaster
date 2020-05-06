/*
 * Air pressure control module
 * 
 * Version History
 * 
 * Version: 0.1 (2019/09) For HeadBlaster user study & gaming
 * Version: 1.0 (2019/10) Add document
 * 
 * Goal: Receive input signal from Unity (or just from SerialPort), 
 *       then output signal(voltage 0V ~ 10V) to air pressure controller (ITV2050), and output on/off signal to solenoid valve (SYJ712)
 *       
 * Current Behavior: Receive input signal from Unity 4Bytes (header,signal1,signal2,signal3)
 *                   Header => let arduino know which strategy we want to use
 *                   signal1,2 (0~255) => convert to "air pressure"  [if MAX_PRESSURE is 700 kPa and signal is 128 then "air pressure" ~= 350kPa] [if MAX_PRESSURE is 700 kPa and signal is 255 then "air pressure" = 700kPa]
 *                                     => ITV2050 can regulate the output air pressure to the target air pressure. (if the target air pressure is smaller than or equal to the air pressure in the tank)
 *                   signal3 => decide which solenoid valve we want to open
 *       
 * Devices: 
 *      1. Arduino nano / Uno ...
 *      2. ITV2050  (air pressure controller)
 *      3. SYJ712   (solenoid valve)
 *      4. relay*n  (depends on how many SYJ712 you want to use)
 *      5. L298N    (output voltage controller)(https://shop.cpu.com.tw/product/46920/info/)
 *      
 *      
 * Execution order(Basic):
 *      1. Initialization (Serial Port, PIN, Mode "explain later")   [F001, F002, F003]
 *      2. Wait for Unity(Serial port) signal, decode the signal, and put the signal into the buffer [F004]
 *      3. Send Byte1 and Byte2 to signal->voltage convert pipeline. [F005->F006]  
 *         ### signal : ((The value of pressure you want to output)/MAX_PRESSURE)*255  Note that signal sholud between 0~255###
 *      4. Convert input signal to fit adt result [F007] (if you don't need adt, just skip this step)
 *      5. Convert signal to voltage [F008] 
 *         ### voltage : The value of voltage L298N should send to air pressure controller ###
 *      6. Convert the value of voltage to the analog signal that can let L298N output the specific voltage. [F009]
 *      7. Send converted signal to L298N, and use L298N's output voltage to control ITV2050 [F005]   (pressure control)
 *      8. Send on/off signal to the solenoid valves [F005] (on/off control)
 *      
 * Function List:     
 *      F001. setup()
 *      F002. pinInitialization()
 *      F003. adtInitialization()   (if you don't need adt, just remove this function and make sure input signal Byte0 always = '0')
 *      F004. serialEvent() <- entrance point
 *      F005. flushBuffer(mode)
 *      F006. convertSignalToAnalog(port, signal)   (pipeline entrance)
 *      F007. convertUnitySignalToMappingSignal(port, UnitySignal)    (if you don't need adt, just remove this function)
 *      F008. convertSignalToVoltage(port, mappingSignal)
 *      F009. convertVoltageToAnalog(port, voltage)
 *      F010. reset()
 *      
 * Input signal format:  
 *      Current Version 1.0 (4 byte per signal)
 *        
 *        Byte0: header (Detail -> function) '0' ~ '3'
 *        Byte1: output pressure ITV2050 (no.1) (0~255)
 *        Byte2: output pressure ITV2050 (no.2) (0~255)
 *        Byte3: solenoid valve on/off (0000____) last 4 bits   [0,0,0,0,Front,Back,Right,Left] (1 : on  ||  0 : off)
 *        
 * Issue:
 *    1. F005 should be separated into two functions (airPressureControl and solenoidValveControl)
 *    2. code of ADT is .... dirty QQ
 *    3. current version arduino just can control "the output pressure related to MAX_PRESSURE". if max_pressure change, unity / other system also have to change their setting.
 *       maybe can build a new version which can receive more bytes (include max_pressure) to make coding easier. 
 *       (Goal: if we use the same hardware (SYJ/ITV/...), we do not have to change the arduino code anywhere) (Future work~~)
*/

// Mode 

class MODE{
  public:
    int currentMode;
    const static int MODE_IDLE = 0;
    const static int MODE_INPUT = 1;
    
    //in MODE_IDLE we can read header(Byte0) to let arduino know which strategy we want to use. 
    const static char HEADER_START_REGULAR_INPUT = '0';
    const static char HEADER_RESET = '1';
    const static char HEADER_DEBUG = '2';
    const static char HEADER_START_SEATED_INPUT = '3';
    const static char HEADER_START_STANDING_INPUT = '4';
};

//ADT Setting (magical numbers~ Detail: HeadBlaster(HCI Lab project CHI'20) )
class ADT{
  public:
    char posture = '0';
    const float SEATED_BACK = 0.33958333;
    const float SEATED_FRONT = 0.33125;
    const float SEATED_LEFT = 0.225;
    const float SEATED_RIGHT = 0.2541666667;
    const float STANDING_BACK = 0.4104167;
    const float STANDING_FRONT = 0.383333;
    const float STANDING_LEFT = 0.291667;
    const float STANDING_RIGHT = 0.322917;
    float seated_back = 0;
    float seated_front = 0;
    float seated_left = 0;
    float seated_right = 0;
    float standing_back = 0;
    float standing_front = 0;
    float standing_left = 0;
    float standing_right = 0;
    int horizontal_direction = 0; // 0 = RIGHT 1 = LEFT
    int longitudinal_direction = 0; // 0 = FRONT 1 = BACK
};





/////////////////
///  Setting  ///
/////////////////

MODE* mode = new MODE(); //mode
ADT* adt = new ADT();    //For adt, maybe you don't need it

//Air Pressure Voltage Setting
const int MAX_PRESSURE = 700;  //KPa
const float MAX_VOLTAGE = 10;  //V

//Data
const int PORT = 9600;     //SerialPort
const int BYTE_LENGTH = 3; //Byte length without header byte
byte buffer[16]; 
int bufferLength; //variable for read signal [F004,F005]

//Debug.log
const char MODE_SERIAL_PRINT = 0; // for print debug signal (0=off || 1=on)

// Hardware & Port Setting : ITV2050 - SYJ712
const int REGULATOR_NUM = 2; // ITV2050 
int outputPin[] = {10, 6, A4, A5, A3, A2};   // {ITV2050-1, ITV2050-2, SYJ-LEFT, SYJ-RIGHT, SYJ-BACK, SYJ-FRONT}
String outputDir[] = {"ITV2050-1", "ITV2050-2", "LEFT", "RIGHT", "BACK", "FRONT"};


//variable for decoding signal (can move these variables into functions) 
float voltage; // [F006]
int analog;    // [F006]
float mappingSignal; // [F006]
float pressure;      // [F008]


////////////////
///   F001   ///
////////////////
void setup()
{
    // put your setup code here, to run once:
    Serial.begin(PORT);  // port         initialization
    Serial.println("Initializing...");
    mode->currentMode = mode->MODE_IDLE;    // mode         initialization
    bufferLength = 0;    // bufferLength initialization
    pinInitialization(); // pin          initialization
    adtInitialization(); // adt          initialization
    Serial.println("Completed!");
}

void loop()
{   
    // Do nothing
    // put your main code here, to run repeatedly:
}


////////////////
///   F002   ///
////////////////
void pinInitialization()
{
  
  for(int i = 0; i< (sizeof(outputPin) / sizeof(int)); ++i)
  {
    pinMode(outputPin[i], OUTPUT);
  }
  reset();
}




////////////////
///   F003   ///
////////////////
void adtInitialization()
{
  // (air_pressure / max_pressure)*255 ==> signal 0~255
  // air_pressure = Absolute Detection Threshold can be noticed.
  // ADT_xxx_ooo = Absolute Detection Threshold (force) can be noticed.
  // 0.263, 5.98, ...., magic number (x)   Coefficients came from curve fitting.
  adt->seated_back = (((adt->SEATED_BACK+0.263)/(5.98*0.001))/MAX_PRESSURE)*255;
  adt->seated_front = (((adt->SEATED_FRONT+0.263)/(5.98*0.001))/MAX_PRESSURE)*255;
  adt->seated_left = (((adt->SEATED_LEFT/2+0.221)/(2.91*0.001))/MAX_PRESSURE)*255;
  adt->seated_right = (((adt->SEATED_RIGHT/2+0.221)/(2.91*0.001))/MAX_PRESSURE)*255;
  adt->standing_back = (((adt->STANDING_BACK+0.263)/(5.98*0.001))/MAX_PRESSURE)*255;
  adt->standing_front = (((adt->STANDING_FRONT+0.263)/(5.98*0.001))/MAX_PRESSURE)*255;
  adt->standing_left = (((adt->STANDING_LEFT/2+0.221)/(2.91*0.001))/MAX_PRESSURE)*255;
  adt->standing_right = (((adt->STANDING_RIGHT/2+0.221)/(2.91*0.001))/MAX_PRESSURE)*255;
}


////////////////
///   F005   ///
////////////////
void flushBuffer(int currentMode)
{
    switch (currentMode)
    {
    case mode->MODE_INPUT: //if mode != MODE_INPUT then do nothing.  (double check)
        
        //ITV2050 output signal
        for(int i = 0; i < REGULATOR_NUM; ++i)
        {  
            analogWrite(outputPin[i], convertSignalToAnalog(i,(int)buffer[i]));
        }
        
        if(MODE_SERIAL_PRINT)
        {
          Serial.print("ITV2050 front / back  : ");
          Serial.print((int)buffer[0] * 100 / 255, DEC);
          Serial.print("%  ");
          Serial.print(int((float((int)buffer[0])/ 255)*MAX_PRESSURE), DEC);
          Serial.print("KPa\nITV2050 left  / right : ");
          Serial.print((int)buffer[1] * 100 / 255, DEC);
          Serial.print("%  ");
          Serial.print(int((float((int)buffer[1])/ 255)*MAX_PRESSURE), DEC);
          Serial.print("KPa\n");
        }

        //SYJ open-close
        for(int i = REGULATOR_NUM ; i < (sizeof(outputPin) / sizeof(int)); ++i)
        {    
          digitalWrite(outputPin[i], buffer[REGULATOR_NUM]&1);
          
          if(MODE_SERIAL_PRINT)
          {
            Serial.print(outputDir[i]);
            Serial.print(" : ");
            Serial.println(buffer[REGULATOR_NUM]&1); 
          }
          
          buffer[REGULATOR_NUM] = buffer[REGULATOR_NUM] >> 1;
        }

        bufferLength = 0;
        break;
    }
}


////////////////
///   F004   ///
////////////////
void serialEvent(){
  /* 
   *  input signal : '1' or '0abc' 
   *  
   *  '0' : HEADER_START_REGULAR_INPUT
   *  '1' : HEADER_RESET
   *  '2' : DEBUG
   *  '3' : HEADER_START_SEATED_INPUT
   *  '4' : HEADER_START_STANDING_INPUT
   *  
   *  a : ITV2050 (front / back) analog signal
   *  b : ITV2050 (left / right) analog signal
   *  c : '0000wxyz' 
   *      w : SYJ712 front (open / close)
   *      x : SYJ712 back  (open / close)
   *      y : SYJ712 right (open / close)
   *      z : SYJ712 left (open / close)
  */
   while (Serial.available())
  {
      char c = Serial.read();
      switch (mode->currentMode)
      {   
          // Next byte should be a header
      case mode->MODE_IDLE:
          switch (c)
          {
          case mode->HEADER_START_REGULAR_INPUT: // '0'
              mode->currentMode = mode->MODE_INPUT;
              adt->posture = '0';
              break;

          case mode->HEADER_RESET: // '1'
              reset();
              break;
          case mode->HEADER_DEBUG: // '2'

              //for debug
              
              break;
          case mode->HEADER_START_SEATED_INPUT: //'3'
              mode->currentMode = mode->MODE_INPUT;
              adt->posture = '3';
              break;
          case mode->HEADER_START_STANDING_INPUT: //'4'
              mode->currentMode = mode->MODE_INPUT;
              adt->posture = '4';
              break;
          }
          break;

      case mode->MODE_INPUT:
          buffer[bufferLength] = c;
          bufferLength++;

          if (bufferLength == BYTE_LENGTH)
          {
              flushBuffer(mode->currentMode);
              mode->currentMode = mode->MODE_IDLE;
          }
          break;
      }
  }
}


////////////////
///   F006   ///
////////////////
int convertSignalToAnalog(int port, int UnitySignal)
{
    /*Decoding Pipeline*/
    mappingSignal = convertUnitySignalToMappingSignal(port, UnitySignal);
    voltage = convertSignalToVoltage(port, mappingSignal);
    analog = convertVoltageToAnalog(port, voltage);
    return analog;
}


////////////////
///   F007   ///
////////////////
float convertUnitySignalToMappingSignal(int port, int UnitySignal)
{
  /*
   * 
   * If Unity send signal to arduino, we must make sure user can feel the feedback.
   * So when signal is too small to be detected, we should make the signal larger (output signal >= ADT).
   * 
  */

  // Update direction  (In version1.0 buffer[REGULATOR_NUM] is Byte3) 
  
  adt->horizontal_direction = buffer[REGULATOR_NUM]&1;     // If you want to make it faster, you can move this code to F005 
  adt->longitudinal_direction = buffer[REGULATOR_NUM]&4;   // If you want to make it faster, you can move this code to F005

  
  if(UnitySignal<=0)
  {
    return 0;
  }
  
  float map_signal = UnitySignal;
  
  // for ADT
  if(adt->posture=='0') // NONE
  {
      // NONE MAPPING
  }
  else if(adt->posture=='3') // SEATED
  {
      if(port==0) // front back
      {
          if(adt->longitudinal_direction) // BACK
          {
             map_signal = ((255-adt->seated_back)/254)*(map_signal-1) + adt->seated_back;
          }
          else // FRONT
          {
             map_signal = ((255-adt->seated_front)/254)*(map_signal-1) + adt->seated_front;
          }
          
      }
      else if(port==1) // left right
      {
          if(adt->horizontal_direction)  // LEFT
          {
             map_signal = ((255-adt->seated_left)/254)*(map_signal-1) + adt->seated_left;
          }
          else  // RIGHT
          {
             map_signal = ((255-adt->seated_right)/254)*(map_signal-1) + adt->seated_right;
          }

      }
  }
  else if(adt->posture=='4') // STANDING
  {
      if(port==0) // front back
      {
          if(adt->longitudinal_direction) // BACK
          {
             map_signal = ((255-adt->standing_back)/254)*(map_signal-1) + adt->standing_back;
          }
          else // FRONT
          {
             map_signal = ((255-adt->standing_front)/254)*(map_signal-1) + adt->standing_front;
          }
      }
      else if(port==1) // left right
      {
          if(adt->horizontal_direction)  // LEFT
          {
            map_signal = ((255-adt->standing_left)/254)*(map_signal-1) + adt->standing_left;
          }
          else  // RIGHT
          {
            map_signal = ((255-adt->standing_right)/254)*(map_signal-1) + adt->standing_right;
          }
      }
  }
  else
  {
     // error or debug
  }
  
  return map_signal;
}


////////////////
///   F008   ///
////////////////
float convertSignalToVoltage(int port, float mappingSignal)
{
    /* 
     *  all magic numbers are the coefficients came from curve fitting.
     *  output the value of voltage which can generate the "pressure"
     *  Note that if you are not using HeadBlaster's system (ITV2050 on the hardware board), you should "fit the function" by userself to get the accurate output.
    */
    pressure = (float(mappingSignal)/255) * MAX_PRESSURE;
    if(port==0)
    {
      if(pressure < 200)
      {
        return (pressure*0.0112 - 0.0826 > 0) ? pressure*0.0112 - 0.0826 : 0;
      }
      else
      {
        return (((pressure-49.569)/66.182) > 0) ? (pressure-49.569)/66.182 : 0;
      }
    }
    else if(port==1)
    {
      return (((pressure-14.405)/73.227) > 0) ? (pressure-14.405)/73.227 : 0;
    }
    else
    {
      return 0;
    }
}


////////////////
///   F009   ///
////////////////
int convertVoltageToAnalog(int port, float v){

  /* 
     *  all magic numbers are the coefficients came from curve fitting.
     *  output the analog signal which L298N can send the "vlotage" to ITV2050
     *  Note that if you are not using HeadBlaster's system (L298N on the hardware board), you should "fit the function" by userself to get the accurate output.
   */
    
  if(port==0) //ITV2050 front back
  { 
    if(v>MAX_VOLTAGE)
    {
      v = MAX_VOLTAGE;
    }
    else
    {
      v = (v-0.194)/0.0455;
    }
  }
  else if(port==1) //ITV2050 left right
  { 
    if(v>MAX_VOLTAGE)
    {
      v = MAX_VOLTAGE;
    }
    else if(v<=4)
    {
      v = (v-0.1305)/0.0464;
    }
    else
    {
      v = (v-0.2967)/0.0443;
    }
  }
  return (int(v) > 0) ? int(v) : 0; //analog
}


////////////////
///   F010   ///
////////////////
void reset()
{
  for(int i = 0; i< (sizeof(outputPin) / sizeof(int)); ++i)
  {
    analogWrite(outputPin[i], 0);
  }
}
