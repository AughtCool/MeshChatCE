// MeshHotspot.ino - pocket Wi-Fi hotspot for MeshChatCE
// (lets a Windows Mobile phone and a Meshtastic node such as a Heltec V3 meet)
//
// Board: any ESP8266 (Wemos D1 Mini, NodeMCU, ...)
// What it does:
//   - creates a 2.4 GHz WPA2 network the 8525 and the Heltec can both join
//   - lists connected devices and their IP addresses in the Serial Monitor
//     (115200 baud) so you can find the Heltec's address
//   - blinks the onboard LED: one blink per connected device, every few seconds
//
// No internet needed - it only gives the devices a place to meet.

#include <ESP8266WiFi.h>
extern "C" {
  #include <user_interface.h>
}

// ---------------- SETTINGS: change these if you like ----------------
const char* NETWORK_NAME = "MeshNet";
const char* PASSWORD     = "changeme1234"; // 8 to 63 characters - CHANGE THIS
const int   CHANNEL      = 6;               // 1, 6 or 11 are good choices
const int   MAX_DEVICES  = 4;               // node + phone + computer + spare
const float TX_POWER_DBM = 20.5;            // Wi-Fi transmit power, 0-20.5.
                                            // 10 is plenty for a pocket kit
                                            // (phone within a few metres) and
                                            // saves battery.
// --------------------------------------------------------------------

IPAddress apIP(192, 168, 4, 1);      // the hotspot's own address
IPAddress subnet(255, 255, 255, 0);

int lastCount = -1;
unsigned long lastReport = 0;

void blinkCount(int n) {
  for (int i = 0; i < n; i++) {
    digitalWrite(LED_BUILTIN, LOW);    // LOW = LED on for most ESP8266 boards
    delay(120);
    digitalWrite(LED_BUILTIN, HIGH);
    delay(200);
  }
}

void listDevices() {
  int count = WiFi.softAPgetStationNum();
  Serial.printf("Devices connected: %d\n", count);

  // Lists devices that have been given an IP address
  int withIP = 0;
  struct station_info* st = wifi_softap_get_station_info();
  while (st != NULL) {
    withIP++;
    IPAddress ip((&st->ip)->addr);
    Serial.printf("  %s   MAC %02X:%02X:%02X:%02X:%02X:%02X\n",
                  ip.toString().c_str(),
                  st->bssid[0], st->bssid[1], st->bssid[2],
                  st->bssid[3], st->bssid[4], st->bssid[5]);
    st = STAILQ_NEXT(st, next);
  }
  wifi_softap_free_station_info();
  if (count > withIP) {
    // note: newer ESP8266 board packages often can't list addresses here,
    // so this can show up even when everything works fine
    Serial.printf("  %d device(s) joined (address not listed)\n",
                  count - withIP);
  }
}

void printDhcp() {
  if (wifi_softap_dhcps_status() == DHCP_STARTED) {
    Serial.println("DHCP (address handout): running");
  } else {
    Serial.println("DHCP (address handout): STOPPED");
  }
}

void setup() {
  Serial.begin(115200);
  delay(200);
  Serial.println();
  Serial.println("MeshHotspot starting...");

  pinMode(LED_BUILTIN, OUTPUT);
  digitalWrite(LED_BUILTIN, HIGH);     // LED off

  WiFi.persistent(false);              // don't wear out flash saving settings
  WiFi.mode(WIFI_AP);                  // hotspot only
  WiFi.setPhyMode(WIFI_PHY_MODE_11G);  // b/g speeds: friendliest for old phones
  WiFi.setOutputPower(TX_POWER_DBM);   // see TX_POWER_DBM above
  WiFi.setSleepMode(WIFI_NONE_SLEEP);  // stay awake for a steady connection

  WiFi.softAPConfig(apIP, apIP, subnet);
  bool ok = WiFi.softAP(NETWORK_NAME, PASSWORD, CHANNEL, false, MAX_DEVICES);

  if (ok) {
    // Also accept older WPA (TKIP) devices, like the 8525 on older ROMs
    // (DHCP is paused during the change and restarted after it)
    wifi_softap_dhcps_stop();
    struct softap_config conf;
    wifi_softap_get_config(&conf);
    conf.authmode = AUTH_WPA_WPA2_PSK;
    wifi_softap_set_config_current(&conf);
    wifi_softap_dhcps_start();

    Serial.printf("Hotspot \"%s\" is up on channel %d\n", NETWORK_NAME, CHANNEL);
    Serial.print("Hotspot address: ");
    Serial.println(WiFi.softAPIP());
    printDhcp();
  } else {
    Serial.println("Hotspot FAILED to start - check the password is 8+ characters");
    while (true) {                     // fast blink = error
      digitalWrite(LED_BUILTIN, !digitalRead(LED_BUILTIN));
      delay(100);
    }
  }
}

void loop() {
  int count = WiFi.softAPgetStationNum();

  // report when someone joins or leaves, and every 10 seconds
  // (addresses are handed out a moment after a device joins)
  static unsigned long lastList = 0;
  if (count != lastCount || millis() - lastList > 10000) {
    lastCount = count;
    lastList = millis();
    listDevices();
    // safety net: if address handout ever stops, turn it back on
    if (wifi_softap_dhcps_status() != DHCP_STARTED) {
      Serial.println("DHCP was stopped - restarting it");
      wifi_softap_dhcps_start();
    }
    printDhcp();
  }

  // status every 5 seconds: one blink per connected device
  if (millis() - lastReport > 5000) {
    lastReport = millis();
    if (count == 0) {
      digitalWrite(LED_BUILTIN, LOW);  // short flash = alive, nobody connected
      delay(30);
      digitalWrite(LED_BUILTIN, HIGH);
    } else {
      blinkCount(count);
    }
  }

  delay(50);
}
