// Remote PC Bridge v3. ESP32-WROOM-32 (4 MB). Arduino ESP32 3.x + ArduinoJson 7.x.
// Sin agente en Windows. No controla alimentación física ni reinicia la torre.
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <HTTPClient.h>
#include <WiFiUdp.h>
#include <ArduinoJson.h>
#include <Preferences.h>
#include <esp_system.h>
#include <time.h>
#include "roots.h"

const int LED_PIN=2;
struct DeviceConfig {
  String wifiSsid,wifiPassword,firebaseHost,apiKey,email,password,uid,towerIp,mac;
  uint16_t basePort=47989;
};
DeviceConfig cfg;
byte macTorre[6]={0};
bool configured=false;
Preferences prefs;
WiFiUDP udp;
IPAddress tower;
String token, lastId, ackId, ackState, lastError;
uint32_t tokenAt=0, lastPoll=0, lastBeat=0, lastWifiTry=0, lastAuthTry=0;
uint32_t ledActivityUntil=0;
uint32_t failures=0, wolCount=0;
bool ackDirty=false, lanConfigured=false, httpOpen=false, httpsOpen=false, rtspOpen=false;
bool wasWifiConnected=false;
int lastHttp=0;
uint64_t uptimeMs=0;
uint32_t prevMillis=0;

bool elapsed(uint32_t now,uint32_t then,uint32_t interval) {return uint32_t(now-then)>=interval;}
bool clockReady() {return time(nullptr)>1767225600;}
void error(const String &value) {lastError=value; Serial.println(value);}
bool parseMac(const String &value,byte out[6]) {
  if(value.length()!=17)return false;
  for(int i=0;i<6;i++) {
    int offset=i*3;
    if(i<5 && value[offset+2]!=':')return false;
    char chunk[3]={value[offset],value[offset+1],0};
    if(!isxdigit(chunk[0])||!isxdigit(chunk[1]))return false;
    out[i]=byte(strtoul(chunk,nullptr,16));
  }
  return true;
}
bool validHost(const String &host) {
  if(host.length()<20||host.indexOf('/')>=0||host.indexOf(':')>=0||host.indexOf(' ')>=0)return false;
  return host.endsWith(".firebasedatabase.app")||host.endsWith(".firebaseio.com");
}
bool parseConfig(JsonDocument &d,DeviceConfig &result,byte outMac[6],IPAddress &outTower) {
  result.wifiSsid=d["wifiSsid"] | "";result.wifiPassword=d["wifiPassword"] | "";
  result.firebaseHost=d["firebaseHost"] | "";result.apiKey=d["apiKey"] | "";
  result.email=d["email"] | "";result.password=d["password"] | "";result.uid=d["uid"] | "";
  result.towerIp=d["towerIp"] | "";result.mac=d["mac"] | "";
  int port=d["basePort"] | 0;
  if(result.wifiSsid.isEmpty()||result.wifiSsid.length()>32||result.wifiPassword.length()>64||
     !validHost(result.firebaseHost)||result.apiKey.isEmpty()||result.email.indexOf('@')<1||
     result.password.isEmpty()||result.uid.isEmpty()||port<1024||port>65514||
     !outTower.fromString(result.towerIp)||outTower==IPAddress(0,0,0,0)||!parseMac(result.mac,outMac))return false;
  result.basePort=uint16_t(port);return true;
}
bool loadConfig() {
  String saved=prefs.getString("config","");if(saved.isEmpty())return false;
  JsonDocument d;if(deserializeJson(d,saved))return false;
  return parseConfig(d,cfg,macTorre,tower);
}
void handleSerial() {
  if(!Serial.available())return;
  String line=Serial.readStringUntil('\n');line.trim();
  if(line=="TRPING1") {Serial.printf("TRPONG1:3.0.0:%s\n",configured?"configured":"setup");return;}
  if(!line.startsWith("TRCFG1:"))return;
  if(line.length()>2500){Serial.println("TRCFG_ERROR:size");return;}
  JsonDocument d;
  if(deserializeJson(d,line.substring(7))){Serial.println("TRCFG_ERROR:json");return;}
  DeviceConfig next;byte nextMac[6];IPAddress nextTower;
  if(!parseConfig(d,next,nextMac,nextTower)){Serial.println("TRCFG_ERROR:fields");return;}
  String encoded;serializeJson(d,encoded);
  if(prefs.putString("config",encoded)==0){Serial.println("TRCFG_ERROR:nvs");return;}
  Serial.println("TRCFG_OK");Serial.flush();delay(300);ESP.restart();
}
// LED integrado (GPIO 2, activo en HIGH):
// Wi-Fi: parpadeo regular; NTP: un destello; Auth/Firebase: dos destellos;
// conectado: fijo; peticiones HTTPS: apagado muy breve; WOL: tres pulsos largos.
void showLed(uint32_t now) {
  if(!configured){uint32_t phase=now%2000;digitalWrite(LED_PIN,(phase<130||(phase>=260&&phase<390)||(phase>=520&&phase<650))?HIGH:LOW);return;}
  if(WiFi.status()!=WL_CONNECTED){digitalWrite(LED_PIN,(now/350)%2?HIGH:LOW);return;}
  uint32_t phase=now%2000;
  if(!clockReady()){digitalWrite(LED_PIN,phase<160?HIGH:LOW);return;}
  if(token.isEmpty() || lastHttp<0 || lastHttp>=400){
    digitalWrite(LED_PIN,(phase<140 || (phase>=300 && phase<440))?HIGH:LOW);return;
  }
  digitalWrite(LED_PIN,int32_t(ledActivityUntil-now)>0?LOW:HIGH);
}

// Todas las peticiones verifican TLS y tienen tiempos limitados. Nunca se imprime el token.
int request(const String &url,const char *method,const String &body,String &out) {
  if(!clockReady()){error("NTP time unavailable: TLS waiting");return -100;}
  WiFiClientSecure tls; tls.setCACert(ROOT_CA); tls.setHandshakeTimeout(6);
  HTTPClient http; http.setConnectTimeout(4000);http.setTimeout(5000);http.setReuse(false);
  if(!http.begin(tls,url)){error("Could not start HTTPS");return -101;}
  if(body.length())http.addHeader("Content-Type","application/json");
  // Pulso corto al iniciar una comunicación con Firebase o Authentication.
  digitalWrite(LED_PIN,LOW);delay(65);digitalWrite(LED_PIN,HIGH);
  int code=strcmp(method,"GET")==0?http.GET():http.sendRequest(method,body);
  out=code>0?http.getString():"";http.end();lastHttp=code;
  ledActivityUntil=millis()+160;
  if(code<200||code>=300){failures++;error(String("HTTPS ")+code);}
  return code;
}
bool authenticate() {
  JsonDocument body;body["email"]=cfg.email;body["password"]=cfg.password;body["returnSecureToken"]=true;
  String encoded,response;serializeJson(body,encoded);
  int code=request(String("https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=")+cfg.apiKey,"POST",encoded,response);
  if(code!=200)return false;
  JsonDocument d; if(deserializeJson(d,response)){error("Auth: invalid JSON");return false;}
  if(String(d["localId"] | "")!=cfg.uid){error("Auth: incorrect device UID");return false;}
  token=d["idToken"].as<String>();tokenAt=millis();if(token.isEmpty()){error("Auth: empty token");return false;}
  Serial.println("Firebase: authenticated");return true;
}
int db(const char *path,const char *method,const String &body,String &out) {
  if(token.isEmpty())return -102;
  int code=request(String("https://")+cfg.firebaseHost+"/bridge/"+path+".json?auth="+token,method,body,out);
  if(code==401)token="";
  return code;
}
bool probe(uint16_t port) {WiFiClient c;bool ok=c.connect(tower,port,700);c.stop();return ok;}
void inspectLan() {if(!lanConfigured)return;httpOpen=probe(cfg.basePort);httpsOpen=probe(cfg.basePort-5);rtspOpen=probe(cfg.basePort+21);}
int sendWol() {
  byte packet[102];memset(packet,0xFF,6);for(int n=0;n<16;n++)memcpy(packet+6+n*6,macTorre,6);
  IPAddress ip=WiFi.localIP(),mask=WiFi.subnetMask(),broadcast;
  for(int i=0;i<4;i++)broadcast[i]=ip[i] | byte(~mask[i]);
  int sent=0;
  for(int i=0;i<3;i++) {
    digitalWrite(LED_PIN,LOW);
    if(udp.beginPacket(broadcast,9)){size_t bytes=udp.write(packet,sizeof(packet));int result=udp.endPacket();if(bytes==sizeof(packet)&&result==1)sent++;}
    delay(180);digitalWrite(LED_PIN,HIGH);delay(180);
  }
  wolCount+=sent;Serial.printf("WOL: %d/3 packets sent\n",sent);return sent;
}
void publishAck() {
  if(!ackDirty)return;
  JsonDocument d;d["id"]=ackId;d["state"]=ackState;d["updatedAt"][".sv"]="timestamp";
  String body,out;serializeJson(d,body);
  if(db("ack","PUT",body,out)==200){ackDirty=false;if(ackId==lastId && prefs.getString("state","")!=ackState)prefs.putString("state",ackState);}
}
void command() {
  String response; if(db("command","GET","",response)!=200)return;
  JsonDocument d;if(deserializeJson(d,response)){error("Command: invalid JSON");return;}if(d.isNull())return;
  String id=d["id"] | "";if(id.length()!=32)return;
  if(id==lastId) {if(ackId!=id){ackId=id;ackState=prefs.getString("state","uncertain_after_restart");ackDirty=true;}return;}
  int64_t created=d["createdAt"] | int64_t(0), now=int64_t(time(nullptr))*1000;
  int ttl=d["ttlSec"] | 0;
  if(String(d["action"] | "")!="wake"||ttl!=90||created<=0||created>now+10000||now-created>int64_t(ttl)*1000) {
    if(ackId!=id){ackId=id;ackState="expired_or_invalid";ackDirty=true;}return;
  }
  // Persistir antes del envío evita repetir tras un reinicio. Una caída aquí puede dejar
  // una orden sin ejecutar: se informa como uncertain_after_restart, nunca como éxito.
  if(prefs.putString("state","uncertain_after_restart")==0 || prefs.putString("lastId",id)==0){error("NVS: could not persist command");ackId=id;ackState="storage_error";ackDirty=true;return;}
  lastId=id;ackId=id;
  int count=sendWol();ackState=count>0?"sent":"udp_failed";ackDirty=true;
  prefs.putString("state",ackState);
}
void heartbeat() {
  inspectLan();JsonDocument d;
  d["version"]="3.0.0";d["updatedAt"][".sv"]="timestamp";d["uptimeSec"]=uptimeMs/1000;
  d["resetReason"]=int(esp_reset_reason());d["rssi"]=WiFi.RSSI();d["ip"]=WiFi.localIP().toString();d["freeHeap"]=ESP.getFreeHeap();d["httpFailures"]=failures;d["lastHttp"]=lastHttp;d["lastError"]=lastError;d["wolPackets"]=wolCount;
  d["lan"]["configured"]=lanConfigured;d["lan"]["ip"]=cfg.towerIp;d["lan"]["http"]=httpOpen;d["lan"]["https"]=httpsOpen;d["lan"]["rtsp"]=rtspOpen;
  String body,out;serializeJson(d,body);db("status","PUT",body,out);
}
void setup() {
  Serial.begin(115200);pinMode(LED_PIN,OUTPUT);digitalWrite(LED_PIN,LOW);
  if(!prefs.begin("torre-bridge",false)){error("NVS unavailable: restart the ESP32");while(true)delay(1000);}
  lastId=prefs.getString("lastId","");if(!lastId.isEmpty()){ackId=lastId;ackState=prefs.getString("state","uncertain_after_restart");ackDirty=true;}
  configured=loadConfig();lanConfigured=configured;
  if(configured){WiFi.mode(WIFI_STA);WiFi.setAutoReconnect(true);WiFi.setSleep(false);WiFi.begin(cfg.wifiSsid.c_str(),cfg.wifiPassword.c_str());}
  configTime(0,0,"time.google.com","pool.ntp.org","time.cloudflare.com");
  lastAuthTry=millis()-30000;lastPoll=millis()-5000;lastBeat=millis()-10000;
  Serial.println(configured?"Remote PC Bridge v3: starting Wi-Fi / NTP / TLS / Firebase":"Remote PC Bridge v3: waiting for USB configuration");
}
void loop() {
  uint32_t now=millis();uptimeMs+=uint32_t(now-prevMillis);prevMillis=now;
  handleSerial();
  showLed(now);
  if(!configured){delay(20);return;}
  if(WiFi.status()!=WL_CONNECTED){if(wasWifiConnected){token="";lastHttp=-103;wasWifiConnected=false;}if(elapsed(now,lastWifiTry,15000)){lastWifiTry=now;WiFi.reconnect();error("Wi-Fi disconnected: retrying");}delay(20);return;}
  wasWifiConnected=true;
  if(!clockReady()){if(elapsed(now,lastWifiTry,15000)){lastWifiTry=now;error("Waiting for NTP time");}delay(50);return;}
  if(token.isEmpty()||elapsed(now,tokenAt,3000000UL)){
    if(elapsed(now,lastAuthTry,30000)){lastAuthTry=now;authenticate();}
    if(token.isEmpty()){delay(50);return;}
  }
  // El backoff limita el tráfico durante cortes; se recupera sin reiniciar el ESP32.
  uint32_t interval=lastHttp<0||lastHttp>=400?15000:5000;
  if(elapsed(millis(),lastPoll,interval)){lastPoll=millis();command();publishAck();}
  if(elapsed(millis(),lastBeat,10000)){lastBeat=millis();heartbeat();}
  delay(20);
}
