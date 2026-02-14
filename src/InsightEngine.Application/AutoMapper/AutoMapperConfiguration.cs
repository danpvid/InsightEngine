using AutoMapper;

namespace InsightEngine.Application.AutoMapper;

public class AutoMapperConfiguration
{
    public static MapperConfiguration RegisterMappings()
    {
        return new MapperConfiguration(cfg =>
        {
            // Adicione seus perfis aqui
            // cfg.AddProfile<DomainToViewModelMappingProfile>();
            // cfg.AddProfile<ViewModelToDomainMappingProfile>();
        });
    }
}
