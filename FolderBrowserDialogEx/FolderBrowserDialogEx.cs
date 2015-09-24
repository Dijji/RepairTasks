// taken and edited from DotNetZip Library, http://dotnetzip.codeplex.com under Ms-PL

// FolderBrowserDialogEx.cs
//
// A replacement for the builtin System.Windows.Forms.FolderBrowserDialog class.
// This one includes an edit box, and also displays the full path in the edit box. 
//
// based on code from http://support.microsoft.com/default.aspx?scid=kb;[LN];306285 
// 
// 20 Feb 2009
//
// ========================================================================================
// Example usage:
// 
// string _folderName = "c:\\dinoch";
// private void button1_Click(object sender, EventArgs e)
// {
//     _folderName = (System.IO.Directory.Exists(_folderName)) ? _folderName : "";
//     var dlg1 = new Ionic.Utils.FolderBrowserDialogEx
//     {
//         Description = "Select a folder for the extracted files:",
//         ShowNewFolderButton = true,
//         ShowEditBox = true,
//         //NewStyle = false,
//         SelectedPath = _folderName,
//         ShowFullPathInEditBox= false,
//     };
//     dlg1.RootFolder = System.Environment.SpecialFolder.MyComputer;
// 
//     var result = dlg1.ShowDialog();
// 
//     if (result == DialogResult.OK)
//     {
//         _folderName = dlg1.SelectedPath;
//         this.label1.Text = "The folder selected was: ";
//         this.label2.Text = _folderName;
//     }
// }
//


namespace Ionic.Utils
{
using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Security.Permissions;
using System.Security;
using System.Text;
using System.Threading;
using ShellLib;

    //[Designer("System.Windows.Forms.Design.FolderBrowserDialogDesigner, System.Design, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"), DefaultEvent("HelpRequest"), SRDescription("DescriptionFolderBrowserDialog"), DefaultProperty("SelectedPath")]
    public class FolderBrowserDialogEx : System.Windows.Forms.CommonDialog
    {
        private static readonly int MAX_PATH = 260;

        // Fields
        private ShellApi.BrowseCallbackProc _callback;
        private string _descriptionText;
        private Environment.SpecialFolder _rootFolder;
        private string _selectedPath;
        private string[] _validExtensions;
        private bool _selectedPathNeedsCheck;
        private bool _showNewFolderButton;
        private bool _showEditBox;
        private bool _showBothFilesAndFolders;
        private bool _newStyle = true;
        private bool _showFullPathInEditBox = true;
        private bool _dontIncludeNetworkFoldersBelowDomainLevel;
        private bool _dontExpandZip;
        private uint _uiFlags;
        private IntPtr _hwndEdit;
        private IntPtr _rootFolderLocation;

        // Events
        //[Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public new event EventHandler HelpRequest
        {
            add
            {
                base.HelpRequest += value;
            }
            remove
            {
                base.HelpRequest -= value;
            }
        }

        // ctor
        public FolderBrowserDialogEx()
        {
            this.Reset();
        }

        // Factory Methods
        public static FolderBrowserDialogEx PrinterBrowser()
        {
            FolderBrowserDialogEx x = new FolderBrowserDialogEx();
            // avoid MBRO comppiler warning when passing _rootFolderLocation as a ref:
            x.BecomePrinterBrowser();
            return x;
        }

        public static FolderBrowserDialogEx ComputerBrowser()
        {
            FolderBrowserDialogEx x = new FolderBrowserDialogEx();
            // avoid MBRO comppiler warning when passing _rootFolderLocation as a ref:
            x.BecomeComputerBrowser();
            return x;
        }

	    // Helpers
	    private void BecomePrinterBrowser()
	    {
            _uiFlags += BrowseFlags.BIF_BROWSEFORPRINTER;
            Description = "Select a printer:";
            ShellApi.SHGetSpecialFolderLocation(IntPtr.Zero, ShellApi.CSIDL.CSIDL_PRINTERS, ref this._rootFolderLocation);
            ShowNewFolderButton = false;
            ShowEditBox = false;
	    }       

	    private void BecomeComputerBrowser()
	    {
            _uiFlags += BrowseFlags.BIF_BROWSEFORCOMPUTER;
            Description = "Select a computer:";
            ShellApi.SHGetSpecialFolderLocation(IntPtr.Zero, ShellApi.CSIDL.CSIDL_NETWORK, ref this._rootFolderLocation);
            ShowNewFolderButton = false;
            ShowEditBox = false;
	    }

        private class BrowseFlags
        {
            public const int BIF_DEFAULT = 0x0000;
            public const int BIF_BROWSEFORCOMPUTER = 0x1000;
            public const int BIF_BROWSEFORPRINTER = 0x2000;
            public const int BIF_BROWSEINCLUDEFILES = 0x4000;
            public const int BIF_BROWSEINCLUDEURLS = 0x0080;
            public const int BIF_DONTGOBELOWDOMAIN = 0x0002;
            public const int BIF_EDITBOX = 0x0010;
            public const int BIF_NEWDIALOGSTYLE = 0x0040;
            public const int BIF_NONEWFOLDERBUTTON = 0x0200;
            public const int BIF_RETURNFSANCESTORS = 0x0008;
            public const int BIF_RETURNONLYFSDIRS = 0x0001;
            public const int BIF_SHAREABLE = 0x8000;
            public const int BIF_STATUSTEXT = 0x0004;
            public const int BIF_UAHINT = 0x0100;
            public const int BIF_VALIDATE = 0x0020;
            public const int BIF_NOTRANSLATETARGETS = 0x0400;
        }

        private static class BrowseForFolderMessages
        {
            // messages FROM the folder browser
            public const int BFFM_INITIALIZED = 1;
            public const int BFFM_SELCHANGED = 2;
            public const int BFFM_VALIDATEFAILEDA = 3;
            public const int BFFM_VALIDATEFAILEDW = 4;
            public const int BFFM_IUNKNOWN = 5;

            // messages TO the folder browser
            public const int BFFM_SETSTATUSTEXT = 0x464;
            public const int BFFM_ENABLEOK = 0x465;
            public const int BFFM_SETSELECTIONA = 0x466;
            public const int BFFM_SETSELECTIONW = 0x467;
        }

        private static Guid IID_IFolderFilterSite = new Guid("{C0A651F5-B48B-11d2-B5ED-006097C686F6}");

        private int FolderBrowserCallback(IntPtr hwnd, int msg, IntPtr lParam, IntPtr lpData)
        {
            switch (msg)
            {
                case BrowseForFolderMessages.BFFM_INITIALIZED:
                    if (this._selectedPath.Length != 0)
                    {
                        PInvoke.User32.SendMessage(new HandleRef(null, hwnd), BrowseForFolderMessages.BFFM_SETSELECTIONW, 1, this._selectedPath);
                        if (this._showEditBox && this._showFullPathInEditBox)
                        {
                            // get handle to the Edit box inside the Folder Browser Dialog
                            _hwndEdit = PInvoke.User32.FindWindowEx(new HandleRef(null, hwnd), IntPtr.Zero, "Edit", null);
                            PInvoke.User32.SetWindowText(_hwndEdit, this._selectedPath);
                        }
                    }
                    break;

                case BrowseForFolderMessages.BFFM_IUNKNOWN:
                    IntPtr iunknown = lParam;
                    IntPtr iFolderFilterSite;

                    if (iunknown == IntPtr.Zero)
                        break;

                    System.Runtime.InteropServices.Marshal.QueryInterface(
                        iunknown,
                        ref IID_IFolderFilterSite,
                        out iFolderFilterSite);

                    Object obj = System.Runtime.InteropServices.Marshal.GetTypedObjectForIUnknown(
                        iFolderFilterSite,
                        System.Type.GetType("ShellLib.IFolderFilterSite"));
                    ShellLib.IFolderFilterSite folderFilterSite = (ShellLib.IFolderFilterSite)obj;

                    FilterByExtension filter = new FilterByExtension();

                    filter.ValidExtensions = _validExtensions;
                    filter.DontExpandZip = _dontExpandZip;

                    folderFilterSite.SetFilter(filter);

                    break;

                case BrowseForFolderMessages.BFFM_SELCHANGED:
                    IntPtr pidl = lParam;
                    if (pidl != IntPtr.Zero)
                    {
                        if (((_uiFlags & BrowseFlags.BIF_BROWSEFORPRINTER) == BrowseFlags.BIF_BROWSEFORPRINTER) ||
                            ((_uiFlags & BrowseFlags.BIF_BROWSEFORCOMPUTER) == BrowseFlags.BIF_BROWSEFORCOMPUTER))
                        {
                            // we're browsing for a printer or computer, enable the OK button unconditionally.
                            PInvoke.User32.SendMessage(new HandleRef(null, hwnd), BrowseForFolderMessages.BFFM_ENABLEOK, 0, 1);
                        }
                        else
                        {
                            //IntPtr pszPath = Marshal.AllocHGlobal(MAX_PATH * Marshal.SystemDefaultCharSize);
                            StringBuilder pszPath = new StringBuilder(MAX_PATH);
                            bool haveValidPath = ShellApi.SHGetPathFromIDList(pidl, pszPath);
                            String displayedPath = pszPath.ToString();
                             // whether to enable the OK button or not. (if file is valid)
                            PInvoke.User32.SendMessage(new HandleRef(null, hwnd), BrowseForFolderMessages.BFFM_ENABLEOK, 0, haveValidPath ? 1 : 0);

                            // Maybe set the Edit Box text to the Full Folder path
                            if (haveValidPath && !String.IsNullOrEmpty(displayedPath))
                            {
                                if (_showEditBox && _showFullPathInEditBox)
                                {
                                    if (_hwndEdit != IntPtr.Zero)
                                        PInvoke.User32.SetWindowText(_hwndEdit, displayedPath);
                                }

                                if ((_uiFlags & BrowseFlags.BIF_STATUSTEXT) == BrowseFlags.BIF_STATUSTEXT)
                                    PInvoke.User32.SendMessage(new HandleRef(null, hwnd), BrowseForFolderMessages.BFFM_SETSTATUSTEXT, 0, displayedPath);
                            }
                        }
                    }
                    break;
            }
            return 0;
        }

        private static ShellApi.IMalloc GetSHMalloc()
        {
            ShellApi.IMalloc[] ppMalloc = new ShellApi.IMalloc[1];
            ShellApi.SHGetMalloc(ppMalloc);
            return ppMalloc[0];
        }

        public override void Reset()
        {
            this._rootFolder = (Environment.SpecialFolder)0;
            this._descriptionText = string.Empty;
            this._selectedPath = string.Empty;
            this._validExtensions = null;
            this._selectedPathNeedsCheck = false;
            this._showNewFolderButton = true;
            this._showEditBox = true;
            this._newStyle = true;
            this._dontIncludeNetworkFoldersBelowDomainLevel = false;
            this._dontExpandZip = false;
            this._hwndEdit = IntPtr.Zero;
            this._rootFolderLocation = IntPtr.Zero;
        }

        protected override bool RunDialog(IntPtr hWndOwner)
        {
            bool result = false;
            if (_rootFolderLocation == IntPtr.Zero)
            {
                ShellApi.SHGetSpecialFolderLocation(hWndOwner, (ShellApi.CSIDL)this._rootFolder, ref _rootFolderLocation);
                if (_rootFolderLocation == IntPtr.Zero)
                {
                    ShellApi.SHGetSpecialFolderLocation(hWndOwner, 0, ref _rootFolderLocation);
                    if (_rootFolderLocation == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("FolderBrowserDialogNoRootFolder");
                    }
                }
            }
            _hwndEdit = IntPtr.Zero;
            //_uiFlags = 0;
            if (_dontIncludeNetworkFoldersBelowDomainLevel)
                _uiFlags += BrowseFlags.BIF_DONTGOBELOWDOMAIN;
            if (this._newStyle)
                _uiFlags += BrowseFlags.BIF_NEWDIALOGSTYLE;
            if (!this._showNewFolderButton)
                _uiFlags += BrowseFlags.BIF_NONEWFOLDERBUTTON;
            if (this._showEditBox)
                _uiFlags += BrowseFlags.BIF_EDITBOX;
            if (this._showBothFilesAndFolders)
                _uiFlags += BrowseFlags.BIF_BROWSEINCLUDEFILES;


            if (Control.CheckForIllegalCrossThreadCalls && (Application.OleRequired() != ApartmentState.STA))
            {
                throw new ThreadStateException("DebuggingException: ThreadMustBeSTA");
            }
            IntPtr pidl = IntPtr.Zero;
            IntPtr hglobal = IntPtr.Zero;
            try
            {
                ShellApi.BROWSEINFO browseInfo = new ShellApi.BROWSEINFO();
                hglobal = Marshal.AllocHGlobal(MAX_PATH * Marshal.SystemDefaultCharSize);
                this._callback = new ShellApi.BrowseCallbackProc(this.FolderBrowserCallback);
                browseInfo.pidlRoot = _rootFolderLocation;
                browseInfo.hwndOwner = hWndOwner;
                browseInfo.pszDisplayName = hglobal;
                browseInfo.lpszTitle = this._descriptionText;
                browseInfo.ulFlags = _uiFlags;
                browseInfo.lpfn = this._callback;
                browseInfo.lParam = IntPtr.Zero;
                browseInfo.iImage = 0;
                pidl = ShellApi.SHBrowseForFolder(ref browseInfo);
                if (((_uiFlags & BrowseFlags.BIF_BROWSEFORPRINTER) == BrowseFlags.BIF_BROWSEFORPRINTER) ||
                ((_uiFlags & BrowseFlags.BIF_BROWSEFORCOMPUTER) == BrowseFlags.BIF_BROWSEFORCOMPUTER))
                {
                    this._selectedPath = Marshal.PtrToStringAuto(browseInfo.pszDisplayName);
                    result = true;
                }
                else
                {
                    if (pidl != IntPtr.Zero)
                    {
                        StringBuilder pszPath = new StringBuilder(MAX_PATH);
                        ShellApi.SHGetPathFromIDList(pidl, pszPath);
                        this._selectedPathNeedsCheck = true;
                        this._selectedPath = pszPath.ToString();
                        result = true;
                    }
                }
            }
            finally
            {
                ShellApi.IMalloc sHMalloc = GetSHMalloc();
                sHMalloc.Free(_rootFolderLocation);
                _rootFolderLocation = IntPtr.Zero;
                if (pidl != IntPtr.Zero)
                {
                    sHMalloc.Free(pidl);
                }
                if (hglobal != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(hglobal);
                }
                this._callback = null;
            }
            return result;
        }

        // Properties
        //[SRDescription("FolderBrowserDialogDescription"), SRCategory("CatFolderBrowsing"), Browsable(true), DefaultValue(""), Localizable(true)]

        /// <summary>
        /// This description appears near the top of the dialog box, providing direction to the user.
        /// </summary>
        public string Description
        {
            get
            {
                return this._descriptionText;
            }
            set
            {
                this._descriptionText = (value == null) ? string.Empty : value;
            }
        }

        //[Localizable(false), SRCategory("CatFolderBrowsing"), SRDescription("FolderBrowserDialogRootFolder"), TypeConverter(typeof(SpecialFolderEnumConverter)), Browsable(true), DefaultValue(0)]
        public Environment.SpecialFolder RootFolder
        {
            get
            {
                return this._rootFolder;
            }
            set
            {
                if (!Enum.IsDefined(typeof(Environment.SpecialFolder), value))
                {
                    throw new InvalidEnumArgumentException("value", (int)value, typeof(Environment.SpecialFolder));
                }
                this._rootFolder = value;
            }
        }

        //[Browsable(true), SRDescription("FolderBrowserDialogSelectedPath"), SRCategory("CatFolderBrowsing"), DefaultValue(""), Editor("System.Windows.Forms.Design.SelectedPathEditor, System.Design, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a", typeof(UITypeEditor)), Localizable(true)]

        /// <summary>
        /// Set or get the selected path.  
        /// </summary>
        public string SelectedPath
        {
            get
            {
                if (((this._selectedPath != null) && (this._selectedPath.Length != 0)) && this._selectedPathNeedsCheck)
                {
                    new FileIOPermission(FileIOPermissionAccess.PathDiscovery, this._selectedPath).Demand();
                    this._selectedPathNeedsCheck = false;
                }
                return this._selectedPath;
            }
            set
            {
                this._selectedPath = (value == null) ? string.Empty : value;
                this._selectedPathNeedsCheck = true;
            }
        }
        
        public string[] ValidExtensions
        { 
            get
            {
                return this._validExtensions;
            }
            set
            {
                this._validExtensions = value;
            }
        }

        //[SRDescription("FolderBrowserDialogShowNewFolderButton"), Localizable(false), Browsable(true), DefaultValue(true), SRCategory("CatFolderBrowsing")]

        /// <summary>
        /// Enable or disable the "New Folder" button in the browser dialog.
        /// </summary>
        public bool ShowNewFolderButton
        {
            get
            {
                return this._showNewFolderButton;
            }
            set
            {
                this._showNewFolderButton = value;
            }
        }

        /// <summary>
        /// Show an "edit box" in the folder browser.
        /// </summary>
        /// <remarks>
        /// The "edit box" normally shows the name of the selected folder.  
        /// The user may also type a pathname directly into the edit box.  
        /// </remarks>
        /// <seealso cref="ShowFullPathInEditBox"/>
        public bool ShowEditBox
        {
            get
            {
                return this._showEditBox;
            }
            set
            {
                this._showEditBox = value;
            }
        }

        /// <summary>
        /// Set whether to use the New Folder Browser dialog style.
        /// </summary>
        /// <remarks>
        /// The new style is resizable and includes a "New Folder" button.
        /// </remarks>
        public bool NewStyle
        {
            get
            {
                return this._newStyle;
            }
            set
            {
                this._newStyle = value;
            }
        }


        public bool DontIncludeNetworkFoldersBelowDomainLevel
        {
            get { return _dontIncludeNetworkFoldersBelowDomainLevel; }
            set { _dontIncludeNetworkFoldersBelowDomainLevel = value; }
        }

        public bool DontExpandZip
        {
            get { return _dontExpandZip; }
            set { _dontExpandZip = value; }
        }
        

        /// <summary>
        /// Show the full path in the edit box as the user selects it. 
        /// </summary>
        /// <remarks>
        /// This works only if ShowEditBox is also set to true. 
        /// </remarks>
        public bool ShowFullPathInEditBox
        {
            get { return _showFullPathInEditBox; }
            set { _showFullPathInEditBox = value; }
        }

        public bool ShowBothFilesAndFolders
        {
            get { return _showBothFilesAndFolders; }
            set { _showBothFilesAndFolders = value; }
        }
    }



    internal static class PInvoke
    {
        static PInvoke() { }

        internal static class User32
        {
            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            public static extern IntPtr SendMessage(HandleRef hWnd, int msg, int wParam, string lParam);

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            public static extern IntPtr SendMessage(HandleRef hWnd, int msg, int wParam, int lParam);

            [DllImport("user32.dll", SetLastError = true)]
            //public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);
            //public static extern IntPtr FindWindowEx(HandleRef hwndParent, HandleRef hwndChildAfter, string lpszClass, string lpszWindow);
            public static extern IntPtr FindWindowEx(HandleRef hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern Boolean SetWindowText(IntPtr hWnd, String text);
        }
    }
}
