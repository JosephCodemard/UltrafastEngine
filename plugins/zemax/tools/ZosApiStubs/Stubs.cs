// COMPILE-ONLY stand-in for the ZOS-API assemblies (see ZosApiStubs.csproj). Bodies throw.
using System;
namespace ZOSAPI_NetHelper { public static class ZOSAPI_Initializer { public static bool Initialize() => throw new NotSupportedException("stub"); public static bool Initialize(string dir) => throw new NotSupportedException("stub"); } }
namespace ZOSAPI
{
    public interface IOpticalSystem { Editors.LDE.ILensDataEditor LDE { get; } Editors.MFE.IMeritFunctionEditor MFE { get; } SystemData.ISystemData SystemData { get; } Tools.IOpticalSystemTools Tools { get; } string SystemFile { get; } bool LoadFile(string f, bool saveIfNeeded); }
    public interface IZOSAPI_Application { IOpticalSystem PrimarySystem { get; } bool IsValidLicenseForAPI { get; } void CloseApplication(); }
    public class ZOSAPI_Connection { public IZOSAPI_Application CreateNewApplication() => throw new NotSupportedException("stub"); public IZOSAPI_Application ConnectAsExtension(int n) => throw new NotSupportedException("stub"); }
    public enum ZemaxSystemUnits { Millimeters, Centimeters, Inches, Meters }
}
namespace ZOSAPI.Editors { public interface IEditorCell { int IntegerValue { get; set; } double DoubleValue { get; set; } string Value { get; set; } } }
namespace ZOSAPI.SystemData
{
    public interface IWavelength { int WavelengthNumber { get; } bool IsActive { get; } bool IsPrimary { get; } double Wavelength { get; set; } double Weight { get; set; } void MakePrimary(); }
    public interface IWavelengths { int NumberOfWavelengths { get; } IWavelength GetWavelength(int position); IWavelength AddWavelength(double Wavelength, double Weight); bool RemoveWavelength(int position); }
    public interface ISDUnitsData { ZOSAPI.ZemaxSystemUnits LensUnits { get; } }
    public interface ISDEnvironmentData { double Temperature { get; } double Pressure { get; } }
    public interface ISystemData { IWavelengths Wavelengths { get; } ISDUnitsData Units { get; } ISDEnvironmentData Environment { get; } }
}
namespace ZOSAPI.Editors.LDE
{
    public enum SurfaceType { Standard, CoordinateBreak, DiffractionGrating, EvenAspheric, Paraxial, Gradient1 }
    public interface ISurface { }
    public interface ISurfaceTypeSettings { bool IsValid { get; } }
    public interface ISurfaceGradient1 : ISurface { double DeltaT { get; set; } double n0 { get; set; } double Nr2 { get; set; } double Nr1 { get; set; } }
    public interface ILDETypeData { bool IgnoreSurface { get; set; } }
    public interface ILDERow { string Material { get; set; } string Comment { get; set; } SurfaceType Type { get; } double Thickness { get; set; } double SemiDiameter { get; set; } string Coating { get; set; } ILDETypeData TypeData { get; } ISurfaceTypeSettings GetSurfaceTypeSettings(SurfaceType type); bool ChangeType(ISurfaceTypeSettings settings); ISurface SurfaceData { get; } }
    public interface ILensDataEditor { int NumberOfSurfaces { get; } ILDERow GetSurfaceAt(int n); bool GetGlobalMatrix(int Surface, out double R11, out double R12, out double R13, out double R21, out double R22, out double R23, out double R31, out double R32, out double R33, out double Xo, out double Yo, out double Zo); int GetIndex(int Surface, int NumberOfWavelengths, double[] indexAtWavelength); }
}
namespace ZOSAPI.Editors.MFE
{
    public enum MeritColumn { Comment, Param1, Param2, Param3, Param4, Param5, Param6, Param7, Param8 }
    public enum MeritOperandType { BLNK, CODA, GBPD, GBPR, GBPS, GBPW, PLEN, RAGA, RAGB, RAGC, RAGX, RAGY, RAGZ, RAID, RAED, RENA, RENB, RENC, REAX, REAY, REAZ }
    public interface IMFERow { void ChangeType(MeritOperandType t); ZOSAPI.Editors.IEditorCell GetOperandCell(MeritColumn c); double Value { get; } }
    public interface IMeritFunctionEditor
    {
        int NumberOfOperands { get; }
        IMFERow GetOperandAt(int OperandNumber);
        IMFERow InsertNewOperandAt(int OperandNumber);
        IMFERow AddOperand();
        bool RemoveOperandAt(int OperandNumber);
        int RemoveOperandsAt(int OperandNumber, int numOperands);
        double CalculateMeritFunction();
        double GetOperandValue(MeritOperandType type, int srf, int wave, double Hx, double Hy, double Px, double Py, double Ex, double Ey);
    }
}
namespace ZOSAPI.Tools { public interface IOpticalSystemTools { RayTrace.IBatchRayTrace OpenBatchRayTrace(); } }
namespace ZOSAPI.Tools.RayTrace
{
    public enum RaysType { Real, Paraxial }
    public enum OPDMode { None, Current, CurrentAndChief }
    public interface IRayTraceNormUnpolData
    {
        bool AddRay(int waveNumber, double Hx, double Hy, double Px, double Py, OPDMode calcOPD);
        bool StartReadingResults();
        bool ReadNextResult(out int rayNumber, out int ErrorCode, out int vignetteCode, out double X, out double Y, out double Z, out double L, out double M, out double N, out double l2, out double m2, out double n2, out double opd, out double intensity);
    }
    public interface IBatchRayTrace
    {
        IRayTraceNormUnpolData CreateNormUnpol(int MaxRays, RaysType rayType, int toSurface);
        bool RunAndWaitForCompletion();
        void Close();
    }
}
