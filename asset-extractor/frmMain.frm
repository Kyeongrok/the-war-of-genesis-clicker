VERSION 5.00
Object = "{F9043C88-F6F2-101A-A3C9-08002B2F49FB}#1.2#0"; "COMDLG32.OCX"
Begin VB.Form frmMain 
   BorderStyle     =   1  '단일 고정
   Caption         =   "G3P2 Unpacker"
   ClientHeight    =   690
   ClientLeft      =   45
   ClientTop       =   330
   ClientWidth     =   3360
   Icon            =   "frmMain.frx":0000
   LinkTopic       =   "Form1"
   MaxButton       =   0   'False
   MinButton       =   0   'False
   ScaleHeight     =   690
   ScaleWidth      =   3360
   StartUpPosition =   2  '화면 가운데
   Begin MSComDlg.CommonDialog cdlOpen 
      Left            =   2040
      Top             =   135
      _ExtentX        =   847
      _ExtentY        =   847
      _Version        =   393216
   End
   Begin VB.CommandButton cmdExit 
      Caption         =   "종료"
      Height          =   375
      Left            =   2250
      TabIndex        =   3
      Top             =   30
      Width           =   1080
   End
   Begin VB.CommandButton cmdExtract 
      Caption         =   "추출"
      Height          =   375
      Left            =   1140
      TabIndex        =   2
      Top             =   30
      Width           =   1080
   End
   Begin VB.CommandButton cmdOpen 
      Caption         =   "열기"
      Height          =   375
      Left            =   30
      TabIndex        =   0
      Top             =   30
      Width           =   1080
   End
   Begin VB.Label lblFileName 
      Appearance      =   0  '평면
      BackColor       =   &H80000005&
      BackStyle       =   0  '투명
      BorderStyle     =   1  '단일 고정
      ForeColor       =   &H80000008&
      Height          =   225
      Left            =   915
      TabIndex        =   4
      Top             =   435
      Width           =   2415
   End
   Begin VB.Label Label1 
      Caption         =   "현재 파일:"
      Height          =   165
      Left            =   30
      TabIndex        =   1
      Top             =   465
      Width           =   840
   End
End
Attribute VB_Name = "frmMain"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

Private Declare Sub CopyMemory Lib "kernel32" Alias "RtlMoveMemory" (Destination As Any, Source As Any, ByVal Length As Long)
Private Declare Function ShellExecute Lib "shell32.dll" Alias "ShellExecuteA" (ByVal hWnd As Long, ByVal lpOperation As String, ByVal lpFile As String, ByVal lpParameters As String, ByVal lpDirectory As String, ByVal nShowCmd As Long) As Long

Private Type FileInfo
    Dummy1 As Integer
    FileStartPos As Long
    Dummy2 As Integer
    FileEndPos As Long
    Dummy3 As Integer
    FileName(0 To 8) As Byte
    PakFile(0 To 12) As Byte
End Type

Dim strFilePath As String
Dim lngFileTotal As Long

Private Sub cmdExit_Click()
    Unload Me
    
End Sub

Private Sub cmdExtract_Click()
    Dim strExtrPath As String, strSrcPath As String
    Dim AFileInfo() As FileInfo
    Dim AbytOutName() As Byte, AbytOutData() As Byte, AbytSrcFile() As Byte
    Dim I As Long, AbytCmp(0 To 0) As Byte, strTemp As String
    
    If strFilePath = vbNullString Then
        Call MsgBox("해제 할 패키지 파일을 선택 해 주세요!", vbExclamation, "오류")
        Exit Sub
    ElseIf Not CBool(Len(Dir(strFilePath))) Then
        Call MsgBox("파일을 찾을 수 없습니다!", vbExclamation, "오류")
        Exit Sub
    End If
    
    cmdOpen.Enabled = False
    cmdExtract.Enabled = False
    cmdExit.Enabled = False
    
    strSrcPath = Left$(strFilePath, InStrRev(strFilePath, "\"))
    strExtrPath = App.Path & "\" & Mid$(strFilePath, InStrRev(strFilePath, "\") + 1)
    strExtrPath = Left$(strExtrPath, Len(strExtrPath) - 4) & "\"
    If Not CBool(Len(Dir(strExtrPath, vbDirectory))) Then MkDir (strExtrPath)
    
    Open strFilePath For Binary As #1
        Get #1, 1, lngFileTotal
        lngFileTotal = lngFileTotal \ &H10000
        Erase AFileInfo
        ReDim AFileInfo(0 To lngFileTotal - 1)
        Get #1, 7, AFileInfo
    Close #1
    
    For I = 1 To lngFileTotal
        With AFileInfo(I - 1)
            Erase AbytOutName, AbytSrcFile
            AbytCmp(0) = 0
            Call revb(.FileName)
            Call revb(.PakFile)
            ReDim AbytOutName(0 To InStrB(.FileName, AbytCmp) - 2)
            ReDim AbytSrcFile(0 To InStrB(.PakFile, AbytCmp) - 2)
            Call CopyMemory(AbytOutName(0), .FileName(0), UBound(AbytOutName) + 1)
            Call CopyMemory(AbytSrcFile(0), .PakFile(0), UBound(AbytSrcFile) + 1)
            
            Open strSrcPath & StrConv(AbytSrcFile, vbUnicode) For Binary As #1
                Erase AbytOutData
                ReDim AbytOutData(0 To .FileEndPos - .FileStartPos)
                Get #1, .FileStartPos + 1, AbytOutData
            Close #1
            
            strTemp = StrConv(AbytOutName, vbUnicode)
            If CBool(Len(Dir(strExtrPath & strTemp))) Then
                Open strExtrPath & Left$(strTemp, InStrRev(strTemp, ".") - 1) & "_2" & Mid$(strTemp, InStrRev(strTemp, ".")) For Binary As #1
            Else
                Open strExtrPath & strTemp For Binary As #1
            End If
                Put #1, 1, AbytOutData
            Close #1
        End With
        DoEvents
    Next I
    
    Call MsgBox("완료 되었습니다!", vbInformation, "확인")
    Call ShellExecute(Me.hWnd, "open", strExtrPath, vbNullString, vbNullString, vbNormalFocus)
    
    cmdOpen.Enabled = True
    cmdExtract.Enabled = True
    cmdExit.Enabled = True
    
End Sub

Private Sub revb(ByRef AbytData() As Byte)
    Dim I As Long
    
    For I = 0 To UBound(AbytData)
        AbytData(I) = Not AbytData(I)
    Next I
    
End Sub
Private Sub cmdOpen_Click()
    Dim lngTemp As Long
    
    On Error GoTo nofile
    
    cdlOpen.CancelError = True
    cdlOpen.Filter = "패키지 인덱스 (*.idx)|*.idx"
    cdlOpen.Flags = cdlOFNHideReadOnly
    cdlOpen.ShowOpen
    
    strFilePath = cdlOpen.FileName
    lblFileName = Mid$(strFilePath, InStrRev(strFilePath, "\") + 1)
    
    Open strFilePath For Binary As #1
        Get #1, 1, lngTemp
    Close #1
    
    lblFileName = lblFileName & " (" & lngTemp \ &H10000 & "Files)"
    
nofile:

End Sub
