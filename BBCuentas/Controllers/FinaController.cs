using BusinessLayer;
using ModelLayer;
using BBCuentas.Models;
using r3Take.DataAccessLayer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace BBCuentas.Controllers
{
    public class FinaController : System.Web.Mvc.Controller
    {
        private Usuario_Business usuarioValid = new Usuario_Business();
        private Contrato_Business contrato = new Contrato_Business();
        private readonly string _apiEDCHistoricosUrl = ConfigurationManager.AppSettings["ApiEDCHistoricosUrl"] ?? "http://172.20.40.61/ApiEDCHistoricos/api/EDCHistoricos";

        [Authorize(Roles = "User")]
        public ActionResult Index(string parametro)
        {
            ViewData["Message"] = parametro;

            // Verificar que el usuario pertenece a la empresa Fina (TipoFina = 1)
            int idCliente = Convert.ToInt32(Request.Cookies["Usuario"].Value.ToString());

            // Verificar si el usuario tiene acceso a Fina
            if (!UsuarioTieneAccesoFina(idCliente))
            {
                return RedirectToAction("Index", "Home", new { parametro = "Acceso no autorizado para empresa Fina" });
            }

            return View();
        }

        [Authorize(Roles = "User")]
        public ActionResult AccountStatement()
        {
            int idCliente = Convert.ToInt32(Request.Cookies["Usuario"].Value.ToString());

            // Verificar acceso a Fina
            if (!UsuarioTieneAccesoFina(idCliente))
            {
                ViewData["Message"] = "Lo sentimos, no cuenta con contratos de GC Conautopcion definidos";
                return RedirectToAction("Index", new { parametro = ViewData["Message"] });
            }

            var contratos = GetUserContracts(idCliente);
            Session["contratosFina"] = contratos;
            return View();
        }

        [Authorize(Roles = "User")]
        public ActionResult Security()
        {
            return View();
        }

        private bool UsuarioTieneAccesoFina(int idCliente)
        {
            try
            {
                // Verificar en la cookie si es usuario de Fina
                var tipoEmpresaCookie = Request.Cookies["TipoEmpresa"];
                if (tipoEmpresaCookie != null && tipoEmpresaCookie.Value == "1")
                {
                    return true;
                }

                // Si no hay cookie, verificar directamente con los datos de contratos
                var usuariosContratos = contrato.ObtieneContratosClientes();
                var usuarioConFina = usuariosContratos.Where(u => u.idUsuario == idCliente && u.TipoFina == 1).Any();

                return usuarioConFina;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private List<Contract> GetUserContracts(int idCliente)
        {
            List<Contract> contractsList = new List<Contract>();
            try
            {
                var list = contrato.ObtieneContratosPorCliente(idCliente);
                foreach (var item in list)
                {
                    contractsList.Add(new Contract(item.nombCompania, item.iContrato, item.grupocliente));
                }
                return contractsList;
            }
            catch (Exception)
            {
                return new List<Contract>();
            }
        }

        [HttpPost]
        public JsonResult GetFinaData()
        {
            try
            {
                int idCliente = Convert.ToInt32(Request.Cookies["Usuario"].Value.ToString());
                var contratos = GetUserContracts(idCliente);

                return Json(new
                {
                    success = true,
                    data = contratos.Select(c => new
                    {
                        ContractNumber = c.ContractNumber,
                        Empresa = c.Empresa,
                        GrupoCliente = c.GrupoCliente
                    })
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al obtener datos de Fina: " + ex.Message
                });
            }
        }

        [HttpPost]
        public JsonResult GetEstadosCuenta(string grupocliente)
        {
            try
            {
                // Verificar acceso de usuario
                int idCliente = Convert.ToInt32(Request.Cookies["Usuario"].Value.ToString());
                if (!UsuarioTieneAccesoFina(idCliente))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Acceso no autorizado"
                    });
                }

                // Usar concatenación Grupo+Cliente para la consulta API
                var result = CallApiWithGetAndBody(grupocliente);

                if (result.Success)
                {
                    // Debug: Mostrar datos que se van a enviar al cliente

                    return Json(new
                    {
                        success = true,
                        data = result.Data,
                        method = "BAT-GET-WITH-BODY"
                    });
                }
                else
                {
                    return Json(new
                    {
                        success = false,
                        message = "Error en API externa:",
                        details = new
                        {
                            API_Error = result.ErrorMessage,
                            GrupoCliente_Tested = grupocliente,
                            Method_Used = "curl externo con GET y BODY"
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Error al consultar estados de cuenta: " + ex.Message
                });
            }
        }

        private ApiResult CallApiWithGetAndBody(string grupocliente)
        {
            // Archivos temporales
            var tempJsonFile = "";
            var tempBatFile = "";
            var tempOutputFile = "";

            try
            {
                // Crear archivos temporales
                tempJsonFile = System.IO.Path.GetTempFileName();
                tempBatFile = System.IO.Path.GetTempFileName().Replace(".tmp", ".bat");
                tempOutputFile = System.IO.Path.GetTempFileName();
                var tempLogFile = System.IO.Path.GetTempFileName().Replace(".tmp", ".log");

                // Crear archivo JSON con el contrato
                var jsonContent = $"{{\"contrato\":\"{grupocliente}\"}}";
                System.IO.File.WriteAllText(tempJsonFile, jsonContent);

                // Crear archivo BAT con comando curl (sin -i para evitar headers)
                var batContent = $@"@echo off
echo === INICIO BAT === > ""{tempLogFile}""
echo Verificando curl... >> ""{tempLogFile}""
where curl >> ""{tempLogFile}"" 2>&1
echo === EJECUTANDO CURL === >> ""{tempLogFile}""
curl.exe -s -X GET -H ""Accept: application/json"" -H ""Content-Type: application/json"" --data @""{tempJsonFile}"" {_apiEDCHistoricosUrl} > ""{tempOutputFile}"" 2>&1
echo === CURL TERMINADO === >> ""{tempLogFile}""
echo Exit code: %ERRORLEVEL% >> ""{tempLogFile}""
";

                System.IO.File.WriteAllText(tempBatFile, batContent);


                // Ejecutar archivo BAT sincrónicamente
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempBatFile,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = System.IO.Path.GetTempPath()
                };


                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    var processFinished = process.WaitForExit(30000); // 30 segundos timeout


                    try
                    {
                    }
                    catch (Exception ex)
                    {
                    }

                    // Verificar si archivo de respuesta existe

                    if (System.IO.File.Exists(tempOutputFile))
                    {
                        var fileInfo = new System.IO.FileInfo(tempOutputFile);
                    }

                    // Leer archivo de respuesta
                    if (System.IO.File.Exists(tempOutputFile))
                    {
                        var output = System.IO.File.ReadAllText(tempOutputFile);

                        // Debug: Mostrar contenido del archivo de respuesta

                        // Procesar respuesta
                        var result = ProcessCurlOutput(output);
                        if (result.Success)
                        {
                            return result;
                        }
                        else
                        {
                        }
                    }
                    else
                    {
                    }
                }

                // Si llegamos aquí, falló el método principal, intentar fallback
                return CallApiWithQueryString(grupocliente, "Método BAT principal falló");
            }
            catch (Exception ex)
            {
                return CallApiWithQueryString(grupocliente, $"Error ejecutando BAT: {ex.Message}");
            }
            finally
            {
                // Limpiar archivos temporales
                CleanupTempFiles(tempJsonFile, tempBatFile, tempOutputFile);
            }
        }

        private ApiResult CallApiWithQueryString(string grupocliente, string primaryError)
        {
            var tempBatFile = "";
            var tempOutputFile = "";

            try
            {
                // Crear archivos temporales para fallback
                tempBatFile = System.IO.Path.GetTempFileName().Replace(".tmp", ".bat");
                tempOutputFile = System.IO.Path.GetTempFileName();

                // URL con querystring
                var fallbackUrl = $"{_apiEDCHistoricosUrl}?contrato={Uri.EscapeDataString(grupocliente)}";

                // Crear archivo BAT para fallback (sin -i para evitar headers)
                var batContent = $@"@echo off
curl.exe -s -X GET -H ""Accept: application/json"" ""{fallbackUrl}"" > ""{tempOutputFile}"" 2>&1
";

                System.IO.File.WriteAllText(tempBatFile, batContent);

                // Debug: Mostrar fallback

                // Ejecutar BAT fallback
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempBatFile,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = System.IO.Path.GetTempPath()
                };

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    process.WaitForExit(30000);

                    // Leer archivo de respuesta fallback
                    if (System.IO.File.Exists(tempOutputFile))
                    {
                        var output = System.IO.File.ReadAllText(tempOutputFile);

                        // Debug: Mostrar respuesta fallback

                        // Procesar respuesta fallback
                        var result = ProcessCurlOutput(output);
                        if (result.Success)
                        {
                            return result;
                        }
                    }
                }

                return new ApiResult
                {
                    Success = false,
                    ErrorMessage = $"Ambos métodos BAT fallaron. Primario: {primaryError}. Fallback: No se obtuvo respuesta válida"
                };
            }
            catch (Exception ex)
            {
                return new ApiResult
                {
                    Success = false,
                    ErrorMessage = $"Error crítico: Primario: {primaryError}. Fallback: {ex.Message}"
                };
            }
            finally
            {
                // Limpiar archivos temporales del fallback
                CleanupTempFiles("", tempBatFile, tempOutputFile);
            }
        }

        private ApiResult ProcessCurlOutput(string curlOutput)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(curlOutput))
                {
                    return new ApiResult
                    {
                        Success = false,
                        ErrorMessage = "Respuesta de curl vacía"
                    };
                }

                // Con -s el output debería ser solo JSON, sin headers
                var jsonResponse = curlOutput.Trim();

                // Debug: Mostrar JSON recibido

                if (!string.IsNullOrWhiteSpace(jsonResponse))
                {
                    try
                    {
                        var apiResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<ApiResponseModel>(jsonResponse);

                        // Debug: Mostrar estructura del objeto deserializado

                        if (apiResponse?.archivos != null)
                        {
                            foreach (var archivo in apiResponse.archivos)
                            {
                            }
                        }

                        return new ApiResult
                        {
                            Success = true,
                            Data = apiResponse,
                            ErrorMessage = null
                        };
                    }
                    catch (Exception jsonEx)
                    {

                        // Si falla la deserialización, tal vez tenemos headers mezclados
                        // Intentar extraer JSON como fallback
                        var fallbackResult = ExtractJsonFromMixedOutput(curlOutput);
                        if (fallbackResult.Success)
                        {
                            return fallbackResult;
                        }

                        return new ApiResult
                        {
                            Success = false,
                            ErrorMessage = $"Error deserializando JSON: {jsonEx.Message}"
                        };
                    }
                }

                return new ApiResult
                {
                    Success = false,
                    ErrorMessage = "Respuesta curl vacía o no válida"
                };
            }
            catch (Exception ex)
            {
                return new ApiResult
                {
                    Success = false,
                    ErrorMessage = $"Error procesando salida de curl: {ex.Message}"
                };
            }
        }

        private ApiResult ExtractJsonFromMixedOutput(string mixedOutput)
        {
            try
            {
                // Fallback: si hay headers mezclados, intentar extraer JSON

                var lines = mixedOutput.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var jsonStartIndex = -1;

                for (int i = 0; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]) && i + 1 < lines.Length)
                    {
                        jsonStartIndex = i + 1;
                        break;
                    }
                }

                if (jsonStartIndex > -1)
                {
                    var jsonResponse = string.Join("", lines.Skip(jsonStartIndex));


                    if (!string.IsNullOrWhiteSpace(jsonResponse))
                    {
                        var apiResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<ApiResponseModel>(jsonResponse);


                        return new ApiResult
                        {
                            Success = true,
                            Data = apiResponse,
                            ErrorMessage = null
                        };
                    }
                }

                return new ApiResult
                {
                    Success = false,
                    ErrorMessage = "No se pudo extraer JSON del output mixto"
                };
            }
            catch (Exception ex)
            {
                return new ApiResult
                {
                    Success = false,
                    ErrorMessage = $"Error extrayendo JSON de output mixto: {ex.Message}"
                };
            }
        }

        private void CleanupTempFiles(string jsonFile, string batFile, string outputFile)
        {
            try
            {
                if (!string.IsNullOrEmpty(jsonFile) && System.IO.File.Exists(jsonFile))
                {
                    System.IO.File.Delete(jsonFile);
                }

                if (!string.IsNullOrEmpty(batFile) && System.IO.File.Exists(batFile))
                {
                    System.IO.File.Delete(batFile);
                }

                if (!string.IsNullOrEmpty(outputFile) && System.IO.File.Exists(outputFile))
                {
                    System.IO.File.Delete(outputFile);
                }
            }
            catch (Exception ex)
            {
            }
        }

        private class ApiResponseModel
        {
            public bool estatus { get; set; }
            public string estadocuenta { get; set; }
            public List<ArchivoModel> archivos { get; set; }
        }

        private class ArchivoModel
        {
            public string mes { get; set; }
            public string Archivo { get; set; }
        }

        [HttpPost]
        public JsonResult InsertFinaContract(Usuario usuario)
        {
            usuario.cEMail = Request.Cookies["Nombre"].Value.ToString();
            usuario.TipoFina = 1; // Establecer TipoFina para Fina
            bool succes = false;
            Contract contract = new Contract();
            string empresa = "Fina";

            try
            {
                var mensajeResp = "";
                DAL dal = new DAL();
                Hashtable hashTableParameters = new Hashtable();

                DataTable dtExixteContrato;
                int idCliente = Convert.ToInt32(Request.Cookies["Usuario"].Value.ToString());


                // PASO 1: Consultar primero el webservice wsValidaEmpresa
                bool webServiceSuccess = false;
                bool consultarTbleqplnsof = false;

                if (usuario.iContrato > 0)
                {

                    // Preparar variables para el webservice (misma lógica que AccountStatementController)
                    WSValidaEmpresa.wsecwebObjClient wsValidaEmpresa = new WSValidaEmpresa.wsecwebObjClient();
                    WSValidaEmpresa.wsEcwebResponse response = new WSValidaEmpresa.wsEcwebResponse();
                    WSValidaEmpresa.wsEcwebRequest request = new WSValidaEmpresa.wsEcwebRequest();

                    int? opiCodigo;
                    string opcMensaje = "";
                    int? opilContrato;
                    int? opilEmpresa;
                    int? opiGrupo;
                    int? opiCliente;

                    request.ipiContrato = usuario.iContrato;
                    request.ipiGrupo = usuario.gpoCte1;
                    request.ipiCliente = usuario.gpoCte2;
                    request.ipcNombre = usuario.cNombre;
                    request.ipcPrimerap = usuario.cPrimerApellido;
                    request.ipcSegundoap = usuario.cSegundoApellido;
                    request.ipiCp = usuario.CP;
                    request.ipcFecha = usuario.DateCte.ToString("yyyy-MM-dd");


                    try
                    {
                        wsValidaEmpresa.wsEcweb(request.ipiContrato, request.ipiGrupo, request.ipiCliente, request.ipcNombre, request.ipcPrimerap, request.ipcSegundoap, request.ipiCp, request.ipcFecha, out opiCodigo, out opcMensaje, out opilContrato, out opilEmpresa, out opiGrupo, out opiCliente);


                        if (opilEmpresa > 0)
                        {
                            webServiceSuccess = true;

                            // Actualizar datos con la respuesta del webservice
                            usuario.TipoFina = Convert.ToInt32(opilEmpresa);
                            usuario.iContrato = Convert.ToInt32(opilContrato);
                            usuario.gpoCte1 = Convert.ToInt32(opiGrupo);
                            usuario.gpoCte2 = Convert.ToInt32(opiCliente);

                        }
                        else
                        {
                            webServiceSuccess = false;
                            consultarTbleqplnsof = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        webServiceSuccess = false;
                        consultarTbleqplnsof = true;
                    }

                    // PASO 2: Si el webservice falló, consultar tbleqplnsof
                    if (consultarTbleqplnsof)
                    {

                        Hashtable hashTableSOF = new Hashtable();
                        hashTableSOF.Add("contratosof", usuario.iContrato);

                        DataTable dtContratoSOF = dal.QueryDT("DS_ECWEB",
                            "SELECT contratosof, cl_grupo, cl_cliente, contratopln, idcte, cie FROM [dbo].[tbleqplnsof] WHERE contratosof = @0",
                            "H:S:contratosof", hashTableSOF, System.Web.HttpContext.Current);


                        if (dtContratoSOF.Rows.Count > 0)
                        {
                            webServiceSuccess = true; // Marcar como exitoso para continuar con la inserción
                            DataRow rowSOF = dtContratoSOF.Rows[0];


                            // Usar los valores de tbleqplnsof
                            usuario.gpoCte1 = Convert.ToInt32(rowSOF["cl_grupo"]);
                            usuario.gpoCte2 = Convert.ToInt32(rowSOF["cl_cliente"]);

                        }
                        else
                        {
                            succes = false;
                            return Json(new { result = succes, mensaje = $"Contrato no encontrado: {usuario.iContrato}", contract = new Contract() });
                        }
                    }
                }

                // Solo continuar si alguna de las validaciones fue exitosa
                if (!webServiceSuccess)
                {
                    succes = false;
                    return Json(new { result = succes, mensaje = $"Contrato no encontrado: {usuario.iContrato}", contract = new Contract() });
                }

                if (usuario.iContrato > 0)
                {
                    // Usar el mismo patrón que funciona en AccountStatementController
                    // Primero verificar si el contrato existe en general
                    hashTableParameters.Add("contrato", usuario.iContrato);
                    dtExixteContrato = dal.QueryDT("DS_ECWEB",
                        "SELECT idUsuario FROM [dbo].[Contratos] WHERE iContrato = @0",
                        "H:S:contrato", hashTableParameters, System.Web.HttpContext.Current);


                    // Si existe el contrato, verificar si es para este usuario específico
                    if (dtExixteContrato.Rows.Count > 0)
                    {
                        // Verificar si alguno de los registros pertenece a este usuario
                        bool contratoExisteParaEsteUsuario = false;
                        foreach (DataRow row in dtExixteContrato.Rows)
                        {
                            int usuarioExistente = Convert.ToInt32(row["idUsuario"]);
                            if (usuarioExistente == idCliente)
                            {
                                contratoExisteParaEsteUsuario = true;
                                break;
                            }
                        }


                        if (contratoExisteParaEsteUsuario)
                        {
                            succes = false;
                            return Json(new { result = succes, mensaje = $"El contrato {usuario.iContrato} ya está registrado para este usuario", contract = new Contract() });
                        }
                    }


                    // Proceder con la inserción
                    usuario.idUsuario = idCliente;


                    // Los campos ya son int, usar directamente (pueden ser 0 si estaban vacíos)
                    int iGrupo = usuario.gpoCte1;
                    int iCliente = usuario.gpoCte2;


                    bool insertResult = usuarioValid.InsertContract(usuario);

                    empresa = contrato.ObtieneEmpresPorId(usuario.TipoFina);

                    // Crear el Contract object que automáticamente calculará la concatenación
                    contract = new Contract(empresa, Convert.ToInt32(usuario.iContrato), Convert.ToString(usuario.gpoCte1) + Convert.ToString(usuario.gpoCte2));

                    // Después de crear el contrato, obtener la concatenación real del webservice
                    // mediante la consulta a la BD que usa el mismo formato que Contrato_Repository


                    if (insertResult)
                    {
                        // Verificar y obtener la concatenación real de la BD usando la misma lógica que Contrato_Repository
                        Hashtable verifyParams = new Hashtable();
                        verifyParams.Add("contrato", usuario.iContrato);
                        verifyParams.Add("usuario", usuario.idUsuario);

                        DataTable dtVerify = dal.QueryDT("DS_ECWEB",
                            "SELECT iContrato, idUsuario, iGrupo, iCliente, iCompania, " +
                            "CONVERT(varchar(20), iGrupo) + RIGHT('00000000' + CONVERT(varchar(20), iCliente),3) AS grupocliente " +
                            "FROM [dbo].[Contratos] WHERE iContrato = @0 AND idUsuario = @1",
                            "H:S:contrato;H:S:usuario", verifyParams, System.Web.HttpContext.Current);

                        if (dtVerify.Rows.Count > 0)
                        {
                            var grupoClienteReal = dtVerify.Rows[0]["grupocliente"].ToString();


                            // Actualizar el contract con la concatenación real de la BD
                            contract = new Contract(empresa, Convert.ToInt32(usuario.iContrato), grupoClienteReal);

                            succes = true;
                            return Json(new
                            {
                                result = succes,
                                mensaje = "Contrato agregado correctamente",
                                contract = contract,
                                dbVerified = true
                            });
                        }
                        else
                        {
                            return Json(new { result = false, mensaje = "Error: No se pudo verificar la inserción en la base de datos", contract = contract });
                        }
                    }
                    else
                    {
                        return Json(new { result = false, mensaje = "Error al guardar contrato en la base de datos", contract = contract });
                    }
                }
                else
                {
                    return Json(new { result = succes, mensaje = "Número de contrato inválido", contract = contract });
                }
            }
            catch (Exception ex)
            {
                return Json(new { result = false, mensaje = "Error al agregar contrato: " + ex.Message, contract = contract });
            }
        }

        private class ApiResult
        {
            public bool Success { get; set; }
            public ApiResponseModel Data { get; set; }
            public string ErrorMessage { get; set; }
        }
    }
}