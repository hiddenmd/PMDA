// DEMO recorder header for PMDA development and tests.
// INVENTED: it follows the annotated-C dialect of vendor recorder headers (tags in comments, bitfields,
// commented-out enums that are still referenced, repeated enumerators) but is not any vendor's format.

enum DEMO_EVENTS
{
   THREAT_NEW  = 1,
   THREAT_EXT  = 2,
   THREAT_UPD  = 3,
   THREAT_DEL  = 5,
   MODE_TYPE   = 7,
   SYNC_TYPE   = 12,
   NAV_TYPE    = 13,
   BIT_TYPE    = 14,
   CM_TYPE     = 20,
// OLD_TYPE    = 21,
   HIDDEN_TYPE = 40,
};

// unsigned 16 bits type
typedef unsigned short   Uword;
// unsigned 32 bits type
typedef unsigned long    Udword;

typedef struct REC_HEADER {
               Uword  _signatureA51;   //<Parse>False
               Uword  _signatureA52;   //<Parse>False
               Uword  _eventId;        //<UserEntityID>
               Uword  _spare;          //<Parse>False
               Udword _tod;            //<EventTime>
               Uword  _length;         //<Length>DataOnly <Unit>8 // [All | DataOnly]
               Uword  _source;         //<HeaderType> //[0-sw 1-hw]
} REC_HDR; //<Header>

typedef struct PAYLOAD_HEADER {
               unsigned long sync_word;    //<Parse>False
               unsigned long body_length;  //<Parse>False
               unsigned long time;         //<Title>Time
} Pay_HDR;

//enum LIFECYCLE
//{
//   Fresh     = 0,
//   Extended  = 1,
//   Refreshed = 2,
//   Dropped   = 3,
//};

//enum SYNC_REASON
//{
//   Power_Up = 0,
//   New_Load = 1,
//   Segment_Full = 2,
//};

enum LIFECYCLE_Color
{
               SkyBlue = 0,   //Fresh
                    //Extended
                    //Refreshed
               Gray = 3,      //Dropped
};

enum YES_NO
{
               0,
               1,
};

enum DROP_REASON
{
               None     = 0,
               Timeout  = 1,
               Merge    = 2,
               Merge    = 3,
               Priority = 4,
};

enum DROP_REASON_Color
{
               Gray = 1,
               Gray = 2,
               Ivory = 4,
};

enum CM_KIND { Chaff = 1, Flare = 2, };

enum MODE_ENUM
{
   Search = 0,
   Track  = 1,
   Ground = 2,
};

enum LIST_KIND
{
               PriList = 0,
               FrqList = 1,
               None    = 2,
               None    = 3,
};

struct lobe_info{
               unsigned lobe_id : 12;  // <ColumnGroup>Lobes
               unsigned quality : 15;  // <Resolution> 0.01 <ColumnGroup>Lobes
               unsigned verdict : 5;   // <Enum>DROP_REASON <ColorEnum>DROP_REASON_Color <ColumnGroup>Lobes
};

struct pri_entry  //<HelpTopic>priEntry.htm
{
               unsigned long pri_value;  // <Resolution>0.1
               unsigned long pri_flags;  //
};

// threat report: the recorder writes events 1, 2, 3 and 5; PMDA merges them into <ID>300
struct threat_struct // <Title>Threats <ID>300 <Debrief>  <Group>Threats.Event   <Level>4 <HelpTopic>threats.htm
{
    Pay_HDR head;
               unsigned status : 32;      // <Status> <Enum>LIFECYCLE <Title>Lifecycle <ColorEnum>LIFECYCLE_Color // 0-new 1-ext 2-upd 3-del
               unsigned event_code : 8;   //<Title>Event_Code
               unsigned : 8;              //<Parse>False
               unsigned threat_id : 16;   //<Title>Threat_ID <Key1> <KeySource>1 <PublishKey>

               // dword 2
               unsigned aoa : 9;          //<Title>AOA <Resolution>0.703125 <Unit>deg
               unsigned power : 8;        //<Title>Power <Unit>dB <Resolution>0.5 <Bias>-100
               unsigned lethal : 1;       //<Enum>YES_NO
               unsigned tracking : 1;     //<Enum>YES_NO
               unsigned launch : 1;       //<Enum>YES_NO <Title>ML
               unsigned : 12;             //<Parse>False

               unsigned long frq;         //<Title>FRQ <Resolution>0.5 <Unit>MHz
               unsigned long pri;         //<Title>PRI <Resolution>0.1 <Unit>us
               unsigned pw : 16;          //<Title>PW <Resolution>0.05 <Unit>us
               unsigned symbol : 16;      //<Title>Symbol <DisplayFormat>X
               char site_name[12];        //<String> <Title>Site
               lobe_info lobes[4];        // <SubStructureBehavior>Same  <ColumnGroup>Lobes
               unsigned spare[2];         // <Parse>False

               // filled in by PMDA
               unsigned drop_code : 32;   //<Title>Drop_Code <Enum>DROP_REASON
               int pri_count;             //<Title>PRI_Count
               int frq_count;             //<Title>FRQ_Count
               pri_entry pris[32];        // <Title>PRI_List <SubStructure>pri_count
               unsigned long frqs[32];    //<Title>FRQ_List <SubStructure>frq_count <Resolution>0.5
               int own_hdg;    // <Resolution> 0.0054931640625 <Title>True.Heading <Unit>deg
               int own_lat;    // <Unit>deg <Resolution>0.00000008381903171539306640625 <Title>Lat <DisplayFormat>##0.####0
               int own_lon;    // <Unit>deg <Resolution>0.00000008381903171539306640625 <Title>Long <DisplayFormat>##0.####0
               int own_alt;    // <Resolution>1 <Title>Baro.Altitude <Unit>feet
};

// delete threat structure
struct threat_drop_struct
{
    Pay_HDR head;
               unsigned event_code : 8;   //
               unsigned reason : 8;       // <Enum>DROP_REASON
               unsigned threat_id : 16;   // identification
};

// extension threat structure
struct threat_ext_struct{
    Pay_HDR head;
               unsigned event_code : 8;   //
               unsigned : 8;              //<Parse>False
               unsigned threat_id : 16;   // identification
               unsigned word_count : 8;   // number of words
               unsigned : 4;              //<Parse>False
               unsigned list_kind : 4;    // <Enum>LIST_KIND
               unsigned : 16;             //<Parse>False
               unsigned long words[64];   // values
};

struct nav_struct //<ID>13 <NavigationLevel>0 <Title>Navigation <Level>2
{
    Pay_HDR head;
               unsigned event_code : 8;   //<Title>Event_Code
               unsigned : 8;              //<Parse>False
               unsigned valid_pos : 1;    //<Title>Valid_Pos
               unsigned valid_att : 1;    //<Title>Valid_Att
               unsigned : 14;             //<Parse>False
               int lat;    // <Unit>deg <Resolution>0.00000008381903171539306640625 <Title>Lat <Latitude>
               int lon;    // <Unit>deg <Resolution>0.00000008381903171539306640625 <Title>Long <Longitude>
               int alt;    // <Resolution>1 <Title>Inertial.Altitude <Unit>feet <Altitude>
               int hdg;    // <Resolution>0.0054931640625 <Title>True.Heading <Unit>deg <Heading>
               int pitch;  // <Resolution>0.0054931640625 <Title>Pitch <Unit>deg <Pitch>
               int roll;   // <Resolution>0.0054931640625 <Title>Roll <Unit>deg <Roll>
               int gs;     // <Resolution>0.0625 <Title>Ground.Speed <Unit>knots
};

//  sync structure
struct sync_struct //<ID>12 <Debrief> <Group>Technician.A  <Title>Synchronization <Level>4
{
    Pay_HDR head;
               unsigned event_code : 8;   //<Title>Event_Code
               unsigned : 8;              //<Parse>False
               unsigned reason : 8;       // <Title>Sync_Reason  <Enum>SYNC_REASON
               unsigned new_flight : 1;   //<Title>New_Flight
               unsigned : 7;              //<Parse>False
               unsigned long sync_code;   //<Title>Sync_Code <DisplayFormat>X
               unsigned sw_minor : 8;     //<Title>SW_Minor
               unsigned sw_major : 8;     //<Title>SW_Major
               unsigned ac_type : 8;      //<Title>AC_Type
               unsigned : 8;              //<Parse>False
               unsigned month : 5;        //<Title>Month
               unsigned day : 6;          //<Title>Day
               unsigned year : 7;         //<Title>Year
               unsigned : 14;             //<Parse>False
               char tail_num[8];          //<Title>Tail_Num  <String>
};

struct mode_struct //<ID>7 <Debrief>  <Group>Threats.Event  <Title>Mode.Of.Operation <Level>4
{
    Pay_HDR head;
               unsigned event_code : 8;   // <Title>Event_Code
               unsigned : 8;              //<Parse>False
               unsigned : 16;             //<Parse>False
               unsigned display : 2;      // <Title>DisplayMode <Enum>MODE_ENUM
               unsigned wow : 1;          // <Title>Weight_On_Wheels
               unsigned training : 1;     // <Title>Training
               unsigned : 28;             //<Parse>False
};

struct cm_struct //<ID>20 <Title>Countermeasures <Level>2
{
    Pay_HDR head;
               unsigned event_code : 8;
               unsigned kind : 8;         // <Enum>CM_KIND
               unsigned count : 8;        // <Title>Count
               unsigned : 8;              //<Parse>False
               unsigned chaff_left : 8;   // <Title>Chaff_Inventory
               unsigned flare_left : 8;   // <Title>Flare_Inventory
               unsigned : 16;             //<Parse>False
};

//  bit structure
struct bit_struct //<ID>14  <Debrief> <Group>Technician.A <Title>Built-In-Test <Level>4
{
    Pay_HDR head;
               unsigned event_code : 8;   //
               unsigned idx : 8;          // index into relevant bit status
               unsigned : 8;              //<Parse>False
               unsigned param_count : 8;  // number of params
               unsigned long params[32];  //<SubStructure>param_count  <Unit>hex <DisplayFormat>X4
};

// kept for completeness, never shown
struct event_40  //<ID>40 <InVisible> <Level>4
{
    Pay_HDR head; //<Parse>False
               unsigned  : 32; //<Parse>False
};

//struct old_struct  //<ID>21   <Level>2
//{
//    Pay_HDR head;
//            unsigned event_code : 8;
//};
