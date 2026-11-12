#include <Servo.h>

// Arduino MEGA 2560 pin mapping
const byte TRIG_PIN = 6;
const byte ECHO_PIN = 7;
const byte DOOR_PIN = 9;
const byte ROOF_PIN = 10;
const byte VENT_PIN = 11;
const byte GAS_PIN = A0;

Servo doorServo;
Servo roofServo;
Servo ventServo;

// Calibrate angles to the actual paper mechanism before attaching the parts.
const int DOOR_OPEN_ANGLE = 90;
const int DOOR_CLOSE_ANGLE = 0;
const int ROOF_OPEN_ANGLE = 90;
const int ROOF_CLOSE_ANGLE = 0;

// Demonstration thresholds; raw ADC readings are NOT gas concentrations.
const int GAS_ON = 600;
const int GAS_OFF = 500;

bool doorOpen = false;
bool roofClosed = false;
bool ventOn = false;
int gasValue = 0;
float distanceCm = -1;
unsigned long lastPersonSeen = 0;
unsigned long lastDistanceCheck = 0;
unsigned long lastGasCheck = 0;
unsigned long lastVentMove = 0;
unsigned long lastReport = 0;
int ventAngle = 90;
int ventDirection = 10;
String commandBuffer = "";

float readDistanceCm() {
  digitalWrite(TRIG_PIN, LOW);
  delayMicroseconds(2);
  digitalWrite(TRIG_PIN, HIGH);
  delayMicroseconds(10);
  digitalWrite(TRIG_PIN, LOW);
  unsigned long t = pulseIn(ECHO_PIN, HIGH, 25000UL);
  if (t == 0) return -1;
  return t * 0.0343f / 2.0f;
}

void handleCommand(String cmd) {
  cmd.trim();
  if (cmd == "ROOF_CLOSE") {
    roofServo.write(ROOF_CLOSE_ANGLE);
    roofClosed = true;
  } else if (cmd == "ROOF_OPEN") {
    roofServo.write(ROOF_OPEN_ANGLE);
    roofClosed = false;
  }
}

void readCommands() {
  while (Serial.available() > 0) {
    char c = (char)Serial.read();
    if (c == '\n') {
      handleCommand(commandBuffer);
      commandBuffer = "";
    } else if (c != '\r') {
      if (commandBuffer.length() < 32) commandBuffer += c;
      else commandBuffer = "";
    }
  }
}

void setup() {
  Serial.begin(9600);
  pinMode(TRIG_PIN, OUTPUT);
  pinMode(ECHO_PIN, INPUT);
  doorServo.attach(DOOR_PIN);
  roofServo.attach(ROOF_PIN);
  ventServo.attach(VENT_PIN);

  doorServo.write(DOOR_CLOSE_ANGLE);
  roofServo.write(ROOF_OPEN_ANGLE);
  ventServo.write(90);
}

void loop() {
  unsigned long now = millis();
  readCommands();

  if (now - lastDistanceCheck >= 100UL) {
    lastDistanceCheck = now;
    distanceCm = readDistanceCm();
    if (distanceCm > 0 && distanceCm <= 20) {
      lastPersonSeen = now;
      if (!doorOpen) {
        doorServo.write(DOOR_OPEN_ANGLE);
        doorOpen = true;
      }
    }
    if (doorOpen && now - lastPersonSeen >= 3000UL) {
      doorServo.write(DOOR_CLOSE_ANGLE);
      doorOpen = false;
    }
  }

  if (now - lastGasCheck >= 100UL) {
    lastGasCheck = now;
    gasValue = analogRead(GAS_PIN);
    if (!ventOn && gasValue >= GAS_ON) ventOn = true;
    else if (ventOn && gasValue <= GAS_OFF) {
      ventOn = false;
      ventAngle = 90;
      ventServo.write(90);
    }
  }

  // Standard SG90 is an angle servo, NOT a continuous rotation fan.
  // Oscillate a lightweight paper vane without blocking the main loop.
  if (ventOn && now - lastVentMove >= 20UL) {
    lastVentMove = now;
    ventAngle += ventDirection;
    if (ventAngle >= 135) { ventAngle = 135; ventDirection = -10; }
    if (ventAngle <= 45) { ventAngle = 45; ventDirection = 10; }
    ventServo.write(ventAngle);
  }

  if (now - lastReport >= 1000UL) {
    lastReport = now;
    Serial.print("DIST:"); Serial.print(distanceCm, 1);
    Serial.print(",GAS:"); Serial.print(gasValue);
    Serial.print(",DOOR:"); Serial.print(doorOpen ? "OPEN" : "CLOSED");
    Serial.print(",ROOF:"); Serial.print(roofClosed ? "CLOSED" : "OPEN");
    Serial.print(",VENT:"); Serial.println(ventOn ? "ON" : "OFF");
  }
}
