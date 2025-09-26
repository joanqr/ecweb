using BusinessLayer;
using ModelLayer;
using BBCuentas.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
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
                    System.Diagnostics.Debug.WriteLine("=== DATOS ENVIADOS AL CLIENTE ===");
                    System.Diagnostics.Debug.WriteLine($"Success: true");
                    System.Diagnostics.Debug.WriteLine($"Data type: {result.Data?.GetType()?.Name ?? "null"}");
                    System.Diagnostics.Debug.WriteLine($"Data: {Newtonsoft.Json.JsonConvert.SerializeObject(result.Data)}");
                    System.Diagnostics.Debug.WriteLine("=== FIN DATOS CLIENTE ===");

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
curl -s -X GET -H ""Accept: application/json"" -H ""Content-Type: application/json"" --data @""{tempJsonFile}"" {_apiEDCHistoricosUrl} > ""{tempOutputFile}"" 2>&1
echo === CURL TERMINADO === >> ""{tempLogFile}""
echo Exit code: %ERRORLEVEL% >> ""{tempLogFile}""
";

                System.IO.File.WriteAllText(tempBatFile, batContent);

                // Debug: Mostrar archivos creados
                System.Diagnostics.Debug.WriteLine("=== ARCHIVOS TEMPORALES CREADOS ===");
                System.Diagnostics.Debug.WriteLine($"JSON File: {tempJsonFile}");
                System.Diagnostics.Debug.WriteLine($"BAT File: {tempBatFile}");
                System.Diagnostics.Debug.WriteLine($"Output File: {tempOutputFile}");
                System.Diagnostics.Debug.WriteLine($"JSON Content: {jsonContent}");
                System.Diagnostics.Debug.WriteLine("=== FIN ARCHIVOS ===");

                // Ejecutar archivo BAT sincrónicamente
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempBatFile,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = System.IO.Path.GetTempPath()
                };

                System.Diagnostics.Debug.WriteLine("=== EJECUTANDO BAT ===");
                System.Diagnostics.Debug.WriteLine($"Comando: {tempBatFile}");
                System.Diagnostics.Debug.WriteLine("=== INICIO EJECUCIÓN ===");

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    var processFinished = process.WaitForExit(30000); // 30 segundos timeout

                    System.Diagnostics.Debug.WriteLine($"=== PROCESO TERMINADO ===");
                    System.Diagnostics.Debug.WriteLine($"Proceso terminó: {processFinished}");
                    System.Diagnostics.Debug.WriteLine($"HasExited: {process.HasExited}");

                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"ExitCode: {process.ExitCode}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error obteniendo ExitCode: {ex.Message}");
                    }

                    // Verificar si archivo de respuesta existe
                    System.Diagnostics.Debug.WriteLine($"=== VERIFICANDO ARCHIVO RESPUESTA ===");
                    System.Diagnostics.Debug.WriteLine($"Archivo esperado: {tempOutputFile}");
                    System.Diagnostics.Debug.WriteLine($"Archivo existe: {System.IO.File.Exists(tempOutputFile)}");

                    if (System.IO.File.Exists(tempOutputFile))
                    {
                        var fileInfo = new System.IO.FileInfo(tempOutputFile);
                        System.Diagnostics.Debug.WriteLine($"Tamaño archivo: {fileInfo.Length} bytes");
                        System.Diagnostics.Debug.WriteLine($"Última modificación: {fileInfo.LastWriteTime}");
                    }

                    // Leer archivo de respuesta
                    if (System.IO.File.Exists(tempOutputFile))
                    {
                        var output = System.IO.File.ReadAllText(tempOutputFile);

                        // Debug: Mostrar contenido del archivo de respuesta
                        System.Diagnostics.Debug.WriteLine("=== CONTENIDO ARCHIVO RESPUESTA COMPLETO ===");
                        System.Diagnostics.Debug.WriteLine($"Longitud: {output.Length} caracteres");
                        System.Diagnostics.Debug.WriteLine("Contenido:");
                        System.Diagnostics.Debug.WriteLine(output);
                        System.Diagnostics.Debug.WriteLine("=== FIN ARCHIVO RESPUESTA ===");

                        // Procesar respuesta
                        var result = ProcessCurlOutput(output);
                        if (result.Success)
                        {
                            return result;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"=== RESULTADO FALLIDO ===");
                            System.Diagnostics.Debug.WriteLine($"Error: {result.ErrorMessage}");
                            System.Diagnostics.Debug.WriteLine("=== FIN RESULTADO FALLIDO ===");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("=== ARCHIVO DE RESPUESTA NO EXISTE ===");
                        System.Diagnostics.Debug.WriteLine("El archivo de salida de curl no se creó");
                        System.Diagnostics.Debug.WriteLine("=== FIN ARCHIVO NO EXISTE ===");
                    }
                }

                // Si llegamos aquí, falló el método principal, intentar fallback
                return CallApiWithQueryString(grupocliente, "Método BAT principal falló");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== ERROR EN CallApiWithGetAndBody: {ex.Message} ===");
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
curl -s -X GET -H ""Accept: application/json"" ""{fallbackUrl}"" > ""{tempOutputFile}"" 2>&1
";

                System.IO.File.WriteAllText(tempBatFile, batContent);

                // Debug: Mostrar fallback
                System.Diagnostics.Debug.WriteLine("=== FALLBACK BAT CREADO ===");
                System.Diagnostics.Debug.WriteLine($"BAT File: {tempBatFile}");
                System.Diagnostics.Debug.WriteLine($"Output File: {tempOutputFile}");
                System.Diagnostics.Debug.WriteLine($"URL: {fallbackUrl}");
                System.Diagnostics.Debug.WriteLine("=== FIN FALLBACK BAT ===");

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
                        System.Diagnostics.Debug.WriteLine("=== FALLBACK RESPUESTA ===");
                        System.Diagnostics.Debug.WriteLine(output);
                        System.Diagnostics.Debug.WriteLine("=== FIN FALLBACK RESPUESTA ===");

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
                System.Diagnostics.Debug.WriteLine("=== JSON DIRECTO (SIN HEADERS) ===");
                System.Diagnostics.Debug.WriteLine($"Longitud JSON: {jsonResponse.Length} caracteres");
                System.Diagnostics.Debug.WriteLine("JSON contenido:");
                System.Diagnostics.Debug.WriteLine(jsonResponse);
                System.Diagnostics.Debug.WriteLine("=== FIN JSON DIRECTO ===");

                if (!string.IsNullOrWhiteSpace(jsonResponse))
                {
                    try
                    {
                        var apiResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<ApiResponseModel>(jsonResponse);

                        // Debug: Mostrar estructura del objeto deserializado
                        System.Diagnostics.Debug.WriteLine("=== JSON DESERIALIZADO EXITOSAMENTE ===");
                        System.Diagnostics.Debug.WriteLine($"Tipo deserializado: {apiResponse?.GetType()?.Name ?? "null"}");
                        System.Diagnostics.Debug.WriteLine($"Estatus: {apiResponse?.estatus}");
                        System.Diagnostics.Debug.WriteLine($"Estado cuenta: {apiResponse?.estadocuenta}");
                        System.Diagnostics.Debug.WriteLine($"Archivos count: {apiResponse?.archivos?.Count ?? 0}");

                        if (apiResponse?.archivos != null)
                        {
                            foreach (var archivo in apiResponse.archivos)
                            {
                                System.Diagnostics.Debug.WriteLine($"  - Mes: {archivo.mes}, Archivo: {archivo.Archivo?.Length ?? 0} caracteres");
                            }
                        }
                        System.Diagnostics.Debug.WriteLine("=== FIN ESTRUCTURA DESERIALIZADA ===");

                        return new ApiResult
                        {
                            Success = true,
                            Data = apiResponse,
                            ErrorMessage = null
                        };
                    }
                    catch (Exception jsonEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"=== ERROR DESERIALIZACIÓN: {jsonEx.Message} ===");

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
                System.Diagnostics.Debug.WriteLine("=== INTENTANDO EXTRAER JSON DE OUTPUT MIXTO ===");

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

                    System.Diagnostics.Debug.WriteLine("=== JSON EXTRAÍDO DE MIXTO ===");
                    System.Diagnostics.Debug.WriteLine(jsonResponse);
                    System.Diagnostics.Debug.WriteLine("=== FIN JSON EXTRAÍDO ===");

                    if (!string.IsNullOrWhiteSpace(jsonResponse))
                    {
                        var apiResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<ApiResponseModel>(jsonResponse);

                        System.Diagnostics.Debug.WriteLine("=== JSON MIXTO DESERIALIZADO EXITOSAMENTE ===");

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
                    System.Diagnostics.Debug.WriteLine($"=== ELIMINADO: {jsonFile} ===");
                }

                if (!string.IsNullOrEmpty(batFile) && System.IO.File.Exists(batFile))
                {
                    System.IO.File.Delete(batFile);
                    System.Diagnostics.Debug.WriteLine($"=== ELIMINADO: {batFile} ===");
                }

                if (!string.IsNullOrEmpty(outputFile) && System.IO.File.Exists(outputFile))
                {
                    System.IO.File.Delete(outputFile);
                    System.Diagnostics.Debug.WriteLine($"=== ELIMINADO: {outputFile} ===");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== ERROR LIMPIANDO ARCHIVOS: {ex.Message} ===");
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

        private class ApiResult
        {
            public bool Success { get; set; }
            public ApiResponseModel Data { get; set; }
            public string ErrorMessage { get; set; }
        }
    }
}